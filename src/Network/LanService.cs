using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace SyncPlayer;

enum ControlDirection { Send, Receive, Both }
record LanWindow(string Id, string Name, bool? Muted = null);
record RemoteTarget(string Id, int Offset = 0, bool? Muted = null);
record LocalAddress(string Address, string Adapter, bool HasGateway);
record DeviceView(string Id, string Name, string Address, LanWindow[] Windows, bool Connected, bool CanSend, bool CanReceive, double? Rtt, string Status);
record ConnectionRequest(string Nonce, string Name, ControlDirection Direction);

sealed record LanPacket
{
    public string App { get; init; } = "SyncPlayer/2";
    public string Type { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Token { get; init; } = "";
    public string Nonce { get; init; } = "";
    public ControlDirection Direction { get; init; }
    public long Seq { get; init; }
    public LanWindow[] Windows { get; init; } = [];
    public RemoteTarget[] Targets { get; init; } = [];
    public WireEvent? Event { get; init; }
}

// Discovery is separate from the ephemeral control port. Beacons cannot grant control.
sealed class LanService : IDisposable
{
    public const int DiscoveryPort = 45873;
    static readonly IPAddress Group = IPAddress.Parse("239.255.44.55");
    readonly string id = Guid.NewGuid().ToString("N");
    string name;
    readonly UdpClient control;
    UdpClient? discovery;
    readonly object gate = new();
    readonly CancellationTokenSource cancel = new();
    readonly Dictionary<string, Peer> peers = new();
    readonly Dictionary<string, Request> incoming = new(), outgoing = new();
    readonly Dictionary<string, (string Peer, long Tick)> pings = new();
    readonly HashSet<IPAddress> joined = new();
    readonly Func<LocalAddress[]> addressProvider;
    LanWindow[] windows = [];
    LocalAddress[] addresses = [];
    long seq;
    bool started, disposed, enabled = true;
    public void SetEnabled(bool value)
    {
        lock (gate) {
            enabled = value;
            if (value) return;
            foreach (var peer in peers.Values.Where(p => p.Connected).ToArray()) Disconnect(peer.Id);
            foreach (var request in incoming.Values.ToArray()) Respond(request.Packet.Nonce, false);
            incoming.Clear(); outgoing.Clear(); pings.Clear();
        }
    }
    public string DiscoveryStatus { get; private set; } = Localization.T("搜索中");
    public int Port => ((IPEndPoint)control.Client.LocalEndPoint!).Port;
    public string Id => id;
    public string Name { get { lock (gate) return name; } }
    public void Rename(string value) { lock (gate) name = DeviceIdentity.Validate(value); Search(); }
    public event Action<WireEvent?, RemoteTarget[], string>? Controlled;
    public event Action<string>? Failed;
    sealed class Peer
    {
        public required string Id, Name;
        public required IPEndPoint Endpoint;
        public LanWindow[] Windows = [];
        public string Token = "";
        public bool Send, Receive, Connected;
        public double? Rtt;
        public long LastSeq;
        public DateTime Seen = DateTime.UtcNow, Heard = DateTime.UtcNow;
        public string Status = Localization.T("未连接");
    }
    sealed record Request(LanPacket Packet, IPEndPoint Endpoint, DateTime Deadline);
    public LanService(string? machineName = null, bool discover = true, Func<LocalAddress[]>? getAddresses = null, int preferredPort = 0)
    {
        name = machineName ?? DeviceIdentity.DefaultName;
        addressProvider = getAddresses ?? ReadAddresses;
        try { control = new UdpClient(new IPEndPoint(IPAddress.Any, preferredPort)); }
        catch (SocketException) when (preferredPort != 0) { control = new UdpClient(new IPEndPoint(IPAddress.Any, 0)); }
        if (discover) {
            try {
                discovery = new UdpClient(AddressFamily.InterNetwork);
                discovery.Client.ExclusiveAddressUse = false;
                discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            } catch (SocketException) { discovery?.Dispose(); discovery = null; DiscoveryStatus = Localization.T("搜索不可用"); }
        } else DiscoveryStatus = Localization.T("搜索已关闭");
        RefreshAddresses();
    }
    public static LocalAddress[] ReadAddresses()
    {
        var found = new List<LocalAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try {
                var properties = nic.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var entry in properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)))
                    found.Add(new(entry.Address.ToString(), nic.Name, gateway));
            } catch (NetworkInformationException) { }
        }
        return SortAddresses(found);
    }
    public static LocalAddress[] SortAddresses(IEnumerable<LocalAddress> source) => source
        .Where(a => IPAddress.TryParse(a.Address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Any) && !IPAddress.IsLoopback(ip))
        .DistinctBy(a => a.Address).OrderByDescending(a => a.HasGateway).ThenBy(a => a.Adapter, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Address).ToArray();
    public string[] Addresses { get { lock (gate) return addresses.Select(a => $"{a.Address}:{Port}").ToArray(); } }
    public DeviceView[] Devices { get { lock (gate) return peers.Values.OrderBy(p => p.Name).Select(p => new DeviceView(p.Id, p.Name, p.Endpoint.ToString(), p.Windows.ToArray(), p.Connected, p.Send, p.Receive, p.Rtt, p.Status)).ToArray(); } }
    public ConnectionRequest[] Requests { get { lock (gate) return incoming.Values.Select(r => new ConnectionRequest(r.Packet.Nonce, r.Packet.Name, r.Packet.Direction)).ToArray(); } }
    public void Publish(IEnumerable<LanWindow> current) { lock (gate) windows = current.Take(64).Select(w => w with { Name = w.Name[..Math.Min(w.Name.Length, 1000)] }).ToArray(); }
    public void Start()
    {
        lock (gate) { if (started) return; started = true; }
        _ = Task.Run(() => Receive(control, false));
        if (discovery != null) _ = Task.Run(() => Receive(discovery, true));
        NetworkChange.NetworkAddressChanged += AddressChanged;
        _ = Task.Run(async () => {
            while (!cancel.IsCancellationRequested) {
                RefreshAddresses(); Search(); Tick();
                try { await Task.Delay(2000, cancel.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    void AddressChanged(object? sender, EventArgs args) { RefreshAddresses(); Search(); }
    public void RefreshAddresses()
    {
        LocalAddress[] fresh;
        try { fresh = SortAddresses(addressProvider()); } catch { fresh = []; }
        lock (gate) {
            addresses = fresh;
            if (discovery == null) return;
            foreach (var removed in joined.Where(ip => !fresh.Any(a => a.Address == ip.ToString())).ToArray()) {
                try { discovery.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(Group, removed)); } catch (SocketException) { }
                joined.Remove(removed);
            }
            foreach (var address in fresh) {
                var ip = IPAddress.Parse(address.Address);
                if (joined.Contains(ip)) continue;
                try { discovery.JoinMulticastGroup(Group, ip); joined.Add(ip); } catch (SocketException) { }
            }
            DiscoveryStatus = fresh.Length == 0 ? Localization.T("无局域网地址") : joined.Count == 0 ? Localization.T("搜索不可用") : Localization.T("搜索中");
        }
    }
    public void Search()
    {
        LocalAddress[] local;
        LanPacket packet;
        lock (gate) { if (disposed || discovery == null) return; local = addresses; packet = Packet("hello"); }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packet);
        foreach (var address in local) {
            try {
                using var sender = new UdpClient(new IPEndPoint(IPAddress.Parse(address.Address), 0));
                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Parse(address.Address).GetAddressBytes());
                sender.Send(bytes, new IPEndPoint(Group, DiscoveryPort));
                sender.EnableBroadcast = true;
                sender.Send(bytes, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            } catch (SocketException) { }
        }
    }
    LanPacket Packet(string type) => new() { Type = type, Id = id, Name = name, Windows = windows.ToArray(), Nonce = Port.ToString() };
    static bool Rights(ControlDirection mode, bool initiator, bool sending) => mode == ControlDirection.Both || (mode == ControlDirection.Send) == (initiator == sending);
    public static string DirectionText(ControlDirection direction) => direction switch { ControlDirection.Send => Localization.T("我控制对方"), ControlDirection.Receive => Localization.T("对方控制我"), _ => Localization.T("双向") };
    public void Connect(string address, ControlDirection direction)
    {
        var endpoint = PeerAddress.Parse(address);
        lock (gate) {
            if (!enabled) throw new InvalidOperationException(Localization.T("请先开启远程连接"));
            if (endpoint.Port == Port && (IPAddress.IsLoopback(endpoint.Address) || addresses.Any(a => a.Address == endpoint.Address.ToString()))) throw new ArgumentException(Localization.T("这是本机地址，请选择另一台设备"));
            if (outgoing.Values.Any(r => r.Endpoint.Equals(endpoint))) return;
            string nonce = Guid.NewGuid().ToString("N");
            var packet = Packet("request") with { Token = Guid.NewGuid().ToString("N"), Nonce = nonce, Direction = direction };
            outgoing[nonce] = new(packet, endpoint, DateTime.UtcNow.AddSeconds(12));
            Send(packet, endpoint);
        }
    }
    public void Respond(string nonce, bool accept)
    {
        lock (gate) {
            if (!incoming.Remove(nonce, out var request)) return;
            if (accept) Establish(request.Packet, request.Endpoint, false);
            Send(Packet(accept ? "accept" : "reject") with { Token = request.Packet.Token, Nonce = nonce, Direction = request.Packet.Direction }, request.Endpoint);
        }
    }
    void Establish(LanPacket packet, IPEndPoint endpoint, bool initiator)
    {
        var peer = Upsert(packet, endpoint);
        peer.Token = packet.Token; peer.Connected = true; peer.LastSeq = 0; peer.Heard = DateTime.UtcNow;
        peer.Send = Rights(packet.Direction, initiator, true); peer.Receive = Rights(packet.Direction, initiator, false);
        peer.Status = peer.Send && peer.Receive ? Localization.T("双向") : peer.Send ? Localization.T("我控制对方") : Localization.T("对方控制我");
    }
    Peer Upsert(LanPacket packet, IPEndPoint endpoint)
    {
        if (!peers.TryGetValue(packet.Id, out var peer)) {
            if (peers.Count >= 128) { var oldest = peers.Values.Where(p => !p.Connected).OrderBy(p => p.Seen).FirstOrDefault(); if (oldest != null) peers.Remove(oldest.Id); else throw new IOException(Localization.T("设备数量过多")); }
            peers[packet.Id] = peer = new() { Id = packet.Id, Name = packet.Name, Endpoint = endpoint };
        }
        peer.Name = packet.Name; peer.Windows = packet.Windows; peer.Seen = DateTime.UtcNow;
        if (!peer.Connected) peer.Endpoint = endpoint;
        return peer;
    }
    public void Disconnect(string peerId)
    {
        lock (gate) {
            if (!peers.TryGetValue(peerId, out var peer)) return;
            if (peer.Connected) Send(Packet("bye") with { Token = peer.Token }, peer.Endpoint);
            peer.Connected = false; peer.Token = ""; peer.Status = Localization.T("已断开");
        }
    }
    public void SendControl(string peerId, WireEvent? operation, RemoteTarget[] targets)
    {
        lock (gate) {
            if (!peers.TryGetValue(peerId, out var peer) || !peer.Connected || !peer.Send || targets.Length == 0) return;
            Send(Packet("control") with { Token = peer.Token, Seq = ++seq, Event = operation, Targets = targets.Take(64).ToArray() }, peer.Endpoint);
        }
    }
    async Task Receive(UdpClient socket, bool beacon)
    {
        while (!cancel.IsCancellationRequested) {
            try {
                var datagram = await socket.ReceiveAsync(cancel.Token);
                if (datagram.Buffer.Length > 60000) continue;
                LanPacket? packet;
                try { packet = JsonSerializer.Deserialize<LanPacket>(datagram.Buffer); } catch (JsonException) { continue; }
                if (packet == null || packet.Id == null || packet.Name == null || packet.Token == null || packet.Nonce == null || packet.Windows == null || packet.Targets == null || packet.App != "SyncPlayer/2" || packet.Id == id || packet.Id.Length is < 1 or > 64 || packet.Name.Length > 128 || packet.Windows.Length > 64 || packet.Targets.Length > 64) continue;
                if (packet.Windows.Any(w => w == null || w.Id == null || w.Name == null || w.Id.Length > 64 || w.Name.Length > 1000) || packet.Targets.Any(t => t == null || t.Id == null || t.Id.Length > 64 || Math.Abs((long)t.Offset) > 86400000)) continue;
                Handle(packet, datagram.RemoteEndPoint, beacon);
            } catch (OperationCanceledException) { break; }
              catch (ObjectDisposedException) { break; }
              catch (Exception ex) { if (!cancel.IsCancellationRequested) Failed?.Invoke(ex.Message); }
        }
    }
    void Handle(LanPacket packet, IPEndPoint endpoint, bool beacon)
    {
        WireEvent? operation = null; RemoteTarget[]? targets = null; string source = "";
        lock (gate) {
            if (!enabled) return;
            if (beacon) {
                if (packet.Type != "hello" || !int.TryParse(packet.Nonce, out int port) || port is < 1 or > 65535) return;
                if (peers.TryGetValue(packet.Id, out var connected) && connected.Connected) return;
                Upsert(packet, new(endpoint.Address, port)); return;
            }
            if (packet.Type == "request") {
                if (packet.Token.Length != 32 || packet.Nonce.Length != 32 || !Enum.IsDefined(packet.Direction)) return;
                if (peers.TryGetValue(packet.Id, out var existing) && existing.Connected && existing.Token == packet.Token) {
                    Send(Packet("accept") with { Token = packet.Token, Nonce = packet.Nonce, Direction = packet.Direction }, endpoint); return;
                }
                if (incoming.Count >= 16) return;
                Upsert(packet, endpoint);
                if (!incoming.ContainsKey(packet.Nonce)) incoming[packet.Nonce] = new(packet, endpoint, DateTime.UtcNow.AddSeconds(12)); return;
            }
            if (packet.Type is "accept" or "reject") {
                if (!outgoing.TryGetValue(packet.Nonce, out var request) || request.Packet.Token != packet.Token || !request.Endpoint.Equals(endpoint)) return;
                outgoing.Remove(packet.Nonce);
                if (packet.Type == "accept") Establish(packet with { Direction = request.Packet.Direction }, endpoint, true);
                else { Upsert(packet, endpoint).Status = Localization.T("对方已拒绝"); Failed?.Invoke(Localization.T("对方未接受连接")); }
                return;
            }
            if (!peers.TryGetValue(packet.Id, out var peer) || !peer.Connected || packet.Token.Length != 32 || peer.Token != packet.Token) return;
            peer.Endpoint = endpoint; peer.Heard = DateTime.UtcNow; peer.Seen = DateTime.UtcNow; peer.Windows = packet.Windows; peer.Name = packet.Name;
            if (packet.Type == "ping") { Send(Packet("pong") with { Token = peer.Token, Nonce = packet.Nonce }, endpoint); return; }
            if (packet.Type == "pong") {
                if (pings.Remove(packet.Nonce, out var ping) && ping.Peer == peer.Id) peer.Rtt = (Stopwatch.GetTimestamp() - ping.Tick) * 1000.0 / Stopwatch.Frequency;
                return;
            }
            if (packet.Type == "bye") { peer.Connected = false; peer.Token = ""; peer.Status = Localization.T("已断开"); return; }
            if (packet.Type != "control" || !peer.Receive || packet.Seq <= peer.LastSeq) return;
            if (packet.Event is { } ev && (ev.cur is < 0 || ev.state.HasValue && ev.state is not (0 or 1 or 2 or -1) || ev.speed is < 200 or > 12000)) return;
            peer.LastSeq = packet.Seq;
            operation = packet.Event; targets = packet.Targets.Where(t => windows.Any(w => w.Id == t.Id)).ToArray(); source = peer.Id;
        }
        if (targets?.Length > 0) Controlled?.Invoke(operation, targets, source);
    }
    void Tick()
    {
        lock (gate) {
            DateTime now = DateTime.UtcNow;
            foreach (var request in outgoing.Values.ToArray()) {
                if (request.Deadline < now) { outgoing.Remove(request.Packet.Nonce); Failed?.Invoke(Localization.T("连接未确认，请检查对方是否已打开 SyncPlayer")); }
                else Send(request.Packet, request.Endpoint);
            }
            foreach (var entry in incoming.Where(p => p.Value.Deadline < now).ToArray()) incoming.Remove(entry.Key);
            foreach (var peer in peers.Values.ToArray()) {
                if (peer.Connected) {
                    if ((now - peer.Heard).TotalSeconds > 10) { peer.Connected = false; peer.Token = ""; peer.Status = Localization.T("连接中断"); peer.Rtt = null; continue; }
                    string nonce = Guid.NewGuid().ToString("N"); pings[nonce] = (peer.Id, Stopwatch.GetTimestamp());
                    Send(Packet("ping") with { Token = peer.Token, Nonce = nonce }, peer.Endpoint);
                } else if ((now - peer.Seen).TotalSeconds > 12 && !incoming.Values.Any(r => r.Packet.Id == peer.Id)) peers.Remove(peer.Id);
            }
            foreach (var entry in pings.Where(p => (Stopwatch.GetTimestamp() - p.Value.Tick) / (double)Stopwatch.Frequency > 10).ToArray()) pings.Remove(entry.Key);
        }
    }
    void Send(LanPacket packet, IPEndPoint endpoint)
    {
        if (disposed) return;
        try { control.Send(JsonSerializer.SerializeToUtf8Bytes(packet), endpoint); }
        catch (SocketException ex) { Failed?.Invoke(Localization.T("网络发送失败：") + ex.Message); }
        catch (ObjectDisposedException) { }
    }
    public void Dispose()
    {
        lock (gate) {
            if (disposed) return;
            foreach (var peer in peers.Values.Where(p => p.Connected)) Send(Packet("bye") with { Token = peer.Token }, peer.Endpoint);
            disposed = true; cancel.Cancel();
            NetworkChange.NetworkAddressChanged -= AddressChanged;
            discovery?.Dispose(); control.Dispose();
        }
    }
}
