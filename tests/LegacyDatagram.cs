using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SyncPlayer;

sealed class LegacyDatagram : IDisposable
{
    readonly string id = Guid.NewGuid().ToString("N");
    readonly UdpClient listener;
    readonly UdpClient sender = new();
    readonly IPEndPoint[] peers;
    readonly CancellationTokenSource cancel = new();
    readonly Dictionary<string, long> seen = new();
    long sequence;
    public event Action<WireEvent, string>? Received;
    public event Action<string>? Failed;
    public LegacyDatagram(string listen, string destinations)
    {
        peers = destinations.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(PeerAddress.Parse).ToArray();
        listener = new UdpClient(PeerAddress.Parse(listen));
    }
    public void Start() => _ = Task.Run(async () => {
        while (!cancel.IsCancellationRequested) {
            try {
                var data = await listener.ReceiveAsync(cancel.Token);
                if (data.Buffer.Length > 4096) continue;
                WireEvent? packet;
                try { packet = JsonSerializer.Deserialize<WireEvent>(data.Buffer); } catch (JsonException) { continue; }
                if (packet == null || packet.id == id || packet.type is not ("event" or "progress")) continue;
                if (packet.id != null) {
                    if (seen.TryGetValue(packet.id, out var last) && packet.seq <= last) continue;
                    if (seen.Count > 128) seen.Clear();
                    seen[packet.id] = packet.seq;
                }
                Received?.Invoke(packet, data.RemoteEndPoint.ToString());
            } catch (OperationCanceledException) { break; }
              catch (ObjectDisposedException) { break; }
              catch (Exception ex) { if (!cancel.IsCancellationRequested) Failed?.Invoke(ex.Message); }
        }
    });
    public void Send(string type, int? position, int? duration, int? state, int? speed = null)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new WireEvent { type = type, cur = position, total = duration, state = state, speed = speed, id = id, seq = ++sequence });
        foreach (var peer in peers) sender.Send(data, peer);
    }
    public void Dispose() { cancel.Cancel(); listener.Dispose(); sender.Dispose(); }
}
