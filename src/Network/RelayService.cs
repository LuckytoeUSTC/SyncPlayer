using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;

namespace SyncPlayer;
record LanWindow(string Id, string Name, bool? Muted = null, bool Primary = false, bool Ready = true);
record RemoteTarget(string Id, int Offset = 0, bool? Muted = null);
record DeviceView(string Id, string Name, string Address, LanWindow[] Windows, bool Connected, bool CanSend, bool CanReceive, double? Rtt, string Status);
record ConnectionRequest(string Nonce, string Name, bool Transfer = false);
sealed record RelayPacket {
    public string Type { get; init; } = "";
    public bool CanControl { get; init; }
    public bool MasterChanged { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Nonce { get; init; } = "";
    public string Session { get; init; } = "";
    public long Seq { get; init; }
    public LanWindow[] Windows { get; init; } = [];
    public RemoteTarget? Target { get; init; }
    public WireEvent? Event { get; init; }
}
sealed class RelayService : IDisposable {
    readonly object gate = new();
    readonly string id = Guid.NewGuid().ToString("N");
    string name, code = "", token = "", session = "";
    bool owner, hasControl, enabled = true, disposed;
    bool pairedBefore;
    long seq, lastSeq;
    string pingNonce = "";
    long pingAt;
    readonly Queue<double> rttSamples = new();
    LanWindow[] windows = [];
    DeviceView? peer;
    ConnectionRequest? request;
    readonly Dictionary<string, ConnectionRequest> joinRequests = new();
    DateTime requestUntil;
    CancellationTokenSource? connection;
    Channel<RelayPacket>? queue;
    Channel<RelayPacket>? controlQueue;
    SemaphoreSlim? sendSignal;
    public string Server { get; set; } = "";
    public string ConnectionStatus { get; private set; } = "创建连接或输入连接码";
    public string Name { get { lock(gate) return name; } }
    public string Id => id;
    public string[] Addresses { get { lock(gate) return code.Length > 0 ? [code] : []; } }
    public DeviceView[] Devices { get { lock(gate) return peer == null ? [] : [peer]; } }
    public ConnectionRequest[] Requests { get { lock(gate) { if (request?.Transfer == true && DateTime.UtcNow > requestUntil) request=null; return joinRequests.Values.Concat(request == null ? [] : new[]{request}).ToArray(); } } }
    public long Generation { get; private set; }
    public event Action<bool>? Paired;
    public event Action? WindowsChanged;
    public bool HasControl { get { lock(gate) return hasControl && peer != null; } }
    public bool IsReceiver { get { lock(gate) return pairedBefore && !hasControl; } }
    public event Action<WireEvent?, RemoteTarget, string>? Controlled;
    public event Action<string>? Failed;
    public RelayService(string? machineName = null) { name = machineName ?? DeviceIdentity.DefaultName; }
    public void Rename(string value) { lock(gate) name = DeviceIdentity.Validate(value); Publish(windows); }
    public void SetEnabled(bool value) { enabled = value; if (!value) Disconnect(); }
    Uri BaseUri() {
        string server = string.IsNullOrWhiteSpace(Server) ? RelayConfiguration.Load() : Server;
        if (!Uri.TryCreate(server.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new ArgumentException(Localization.T("中转地址须为 https:// 地址"));
        return uri;
    }
    public void Create() => Begin(true, "");
    public void Connect(string address) {
        string normalized = address.Trim().Replace("-", "").ToUpperInvariant();
        if (normalized.Length != 12 || normalized.Any(c => !"23456789ABCDEFGHJKLMNPQRSTUVWXYZ".Contains(c))) throw new ArgumentException(Localization.T("请输入完整的12位连接码"));
        Begin(false, normalized);
    }
    void Begin(bool master, string room) {
        if (!enabled) throw new InvalidOperationException(Localization.T("请先开启远程连接"));
        var endpoint = BaseUri();
        Disconnect();
        lock(gate) {
            owner = master; hasControl = master; code = room; token = Guid.NewGuid().ToString("N");
            connection = new(); var ct = connection.Token;
            ConnectionStatus = master ? "正在创建连接" : "正在加入";
            _ = Task.Run(() => Run(endpoint, master, ct));
        }
    }
    async Task Run(Uri endpoint, bool master, CancellationToken ct) {
        try {
            if (master) {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var response = await http.PostAsync(new Uri(endpoint, "rooms"), null, ct);
                response.EnsureSuccessStatusCode();
                var room = await response.Content.ReadFromJsonAsync<Dictionary<string,string>>(cancellationToken: ct) ?? throw new IOException(Localization.T("创建连接失败"));
                lock(gate) { if(ct.IsCancellationRequested) return; code = room["code"]; token = room["token"]; }
            }
            while (!ct.IsCancellationRequested) {
                try {
                    using var ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
                    ws.Options.CollectHttpResponseDetails = true;
                    string currentCode, currentToken;
                    lock(gate) { currentCode = code; currentToken = token; ClearPeer(); }
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + currentToken);
                    var uri = new UriBuilder(new Uri(endpoint, $"rooms/{currentCode}/{(master ? "owner" : "guest")}")) { Scheme = endpoint.Scheme == "https" ? "wss" : "ws" };
                    try { await ws.ConnectAsync(uri.Uri, ct); }
                    catch(WebSocketException) when(ws.HttpStatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.Forbidden) {
                        ConnectionStatus = "连接码无效或连接已过期，请重新发起"; Failed?.Invoke(ConnectionStatus); return;
                    }
                    var outgoing = Channel.CreateUnbounded<RelayPacket>(new UnboundedChannelOptions { SingleReader = true });
                    var controls = Channel.CreateBounded<RelayPacket>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
                    using var signal = new SemaphoreSlim(0);
                    lock(gate) { queue = outgoing; controlQueue = controls; sendSignal = signal; ConnectionStatus = master ? "等待另一台电脑加入（连接码10分钟内有效）" : "等待主控接受"; }
                    using var socketCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var send = Task.Run(async () => {
                        while (!socketCancel.IsCancellationRequested) {
                            await signal.WaitAsync(socketCancel.Token);
                            if (outgoing.Reader.TryRead(out var p) || controls.Reader.TryRead(out p)) await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(p), WebSocketMessageType.Text, true, socketCancel.Token);
                        }
                    }, socketCancel.Token);
                    var measure = MeasureRtt(outgoing, socketCancel.Token);
                    Enqueue(new() { Type = "hello", Id = id, Name = Name, Windows = windows });
                    try {
                        byte[] buffer = new byte[65536];
                        while(ws.State == WebSocketState.Open && !ct.IsCancellationRequested) {
                            using var message = new MemoryStream(); WebSocketReceiveResult part;
                            do {
                                part = await ws.ReceiveAsync(buffer, socketCancel.Token);
                                if(part.MessageType == WebSocketMessageType.Close) throw new IOException(Localization.T("连接已关闭"));
                                if(part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 60000) throw new IOException(Localization.T("中转消息无效"));
                                message.Write(buffer, 0, part.Count);
                            } while(!part.EndOfMessage);
                            Handle(JsonSerializer.Deserialize<RelayPacket>(message.ToArray()));
                        }
                    } finally { lock(gate) { if(queue == outgoing) { queue=null; controlQueue=null; sendSignal=null; } } socketCancel.Cancel(); outgoing.Writer.TryComplete(); controls.Writer.TryComplete(); try { await measure; } catch(OperationCanceledException) { } try { await send; } catch(OperationCanceledException) { } catch(WebSocketException) { } }
                } catch(Exception ex) when(!ct.IsCancellationRequested) {
                    lock(gate) { queue = null; ClearPeer(); ConnectionStatus = "正在重连"; }
                    Failed?.Invoke(Localization.F("中转连接失败：{0}", ex.Message));
                    await Task.Delay(3000, ct);
                }
            }
        } catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
          catch(Exception ex) { if(!ct.IsCancellationRequested) { ConnectionStatus = "创建连接失败，请检查网络后重试"; Failed?.Invoke(ex.Message); } }
    }
    void Enqueue(RelayPacket packet) { lock(gate) { if(queue == null)return; var destination = packet.Type == "control" ? controlQueue : queue; if(destination?.Writer.TryWrite(packet) == true)sendSignal?.Release(); } }
    void ResetRtt() { pingNonce = ""; pingAt = 0; rttSamples.Clear(); }
    void ClearPeer() { peer=null; request=null; joinRequests.Clear(); session=""; ResetRtt(); Generation++; }
    async Task MeasureRtt(Channel<RelayPacket> outgoing, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            lock(gate) {
                if (queue == outgoing && peer != null && session.Length > 0) {
                    if (pingNonce.Length > 0 && Stopwatch.GetElapsedTime(pingAt).TotalSeconds >= 6) { ResetRtt(); peer = peer with { Rtt = null }; }
                    if (pingNonce.Length == 0) {
                        pingNonce = Guid.NewGuid().ToString("N"); pingAt = Stopwatch.GetTimestamp();
                        Enqueue(new() { Type = "ping", Session = session, Nonce = pingNonce });
                    }
                }
            }
            await Task.Delay(2000, ct);
        }
    }
    public void Publish(IEnumerable<LanWindow> current) {
        lock(gate) { windows = current.Where(w => w.Primary).Take(1).ToArray(); Enqueue(new() { Type="hello", Id=id, Name=name, Windows=windows }); }
    }
    public void RequestMaster() { lock(gate) { if(peer == null || hasControl) return; Enqueue(new() { Type="master-request", Session=session }); ConnectionStatus="等待对方批准主控申请"; } }
    public void Respond(string nonce, bool accept) { lock(gate) { bool transfer=request?.Nonce == nonce && request.Transfer; if(transfer ? !hasControl : !owner || !joinRequests.ContainsKey(nonce)) return; Enqueue(new() { Type=transfer ? (accept ? "master-accept" : "master-reject") : (accept ? "accept" : "reject"), Nonce=nonce, Session=session }); if(transfer)request=null; else joinRequests.Remove(nonce); } }
    public void Disconnect() { lock(gate) { connection?.Cancel(); connection?.Dispose(); connection=null; queue=null; controlQueue=null; sendSignal=null; pairedBefore=false; ClearPeer(); code=""; ConnectionStatus="已断开，可重新创建或加入"; } }
    public void SendControl(string peerId, WireEvent? operation, RemoteTarget target) {
        lock(gate) { if(!hasControl || peer is not { Connected:true } || peer.Id != peerId || session.Length == 0) return; Enqueue(new() { Type="control", Session=session, Seq=++seq, Event=operation, Target=target }); }
    }
    void Handle(RelayPacket? p) {
        if(p == null) return;
        lock(gate) {
            if (p.Type is "ping" or "pong") {
                if (peer == null || p.Session != session || session.Length == 0 || p.Nonce.Length != 32) return;
                if (p.Type == "ping") { Enqueue(new() { Type = "pong", Session = session, Nonce = p.Nonce }); return; }
                if (p.Nonce != pingNonce) return;
                double elapsed = Stopwatch.GetElapsedTime(pingAt).TotalMilliseconds;
                pingNonce = "";
                if (elapsed >= 6000) { ResetRtt(); peer = peer with { Rtt = null }; return; }
                rttSamples.Enqueue(elapsed); while (rttSamples.Count > 5) rttSamples.Dequeue();
                var samples = rttSamples.Order().ToArray();
                peer = peer with { Rtt = (samples[(samples.Length - 1) / 2] + samples[samples.Length / 2]) / 2 };
                return;
            }
            if(p.Type == "master-request" && hasControl && peer != null && p.Session == session) { request=new(p.Nonce,p.Name,true); requestUntil=DateTime.UtcNow.AddSeconds(30); return; }
            if(p.Type == "master-pending") { ConnectionStatus="等待对方批准主控申请（30秒内有效）"; return; }
            if(p.Type == "master-denied") { ConnectionStatus="主控申请被拒绝或已超时"; Failed?.Invoke(ConnectionStatus); return; }
            if(p.Type == "request" && owner && peer == null && p.Nonce.Length <= 64) { joinRequests[p.Nonce] = new(p.Nonce,p.Name); return; }
            if(p.Type == "request-cancelled") { joinRequests.Remove(p.Nonce); return; }
            if(p.Type == "paired") { ResetRtt(); pairedBefore=true; session=p.Session; hasControl=p.CanControl; Generation++; seq=lastSeq=0; request=null; joinRequests.Clear(); peer=new(p.Id,p.Name,code,p.Windows,true,hasControl,!hasControl,null,hasControl ? "主控" : "受控"); ConnectionStatus="已连接"; Paired?.Invoke(p.MasterChanged); return; }
            if(p.Type == "hello" && peer != null) { peer=peer with { Name=p.Name, Windows=p.Windows }; WindowsChanged?.Invoke(); return; }
            if(p.Type == "offline") { ClearPeer(); ConnectionStatus="正在重连"; return; }
            if(p.Type == "rejected") { ConnectionStatus="主控已拒绝，请重新加入"; Failed?.Invoke(ConnectionStatus); connection?.Cancel(); return; }
            if(p.Type != "control" || hasControl || peer == null || p.Session != session || p.Seq <= lastSeq) return;
            if(p.Target == null || Math.Abs((long)p.Target.Offset)>86400000 || !windows.Any(w=>w.Primary && w.Ready && w.Id==p.Target.Id)) return;
            if(p.Event is {} ev && (ev.cur is < 0 || ev.speed is < 200 or > 12000 || ev.state.HasValue && ev.state is not (0 or 1 or 2 or -1))) return;
            lastSeq=p.Seq; Controlled?.Invoke(p.Event,p.Target,peer.Id);
        }
    }
    public void Dispose() { if(disposed) return; disposed=true; Disconnect(); }
}
