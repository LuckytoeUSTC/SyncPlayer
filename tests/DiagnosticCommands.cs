using System.Diagnostics;
using System.Text;

namespace SyncPlayer;

static class DiagnosticCommands
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--boundary-test")) { ApplicationConfiguration.Initialize(); BoundaryRegression.Run(); return true; }
        if (args.Contains("--language-test")) { ApplicationConfiguration.Initialize(); LocalizationRegression.Run(); return true; }
        if (args.Contains("--lan-test")) { LanRegression.Run(); return true; }

        if (args.Contains("--video-test") || args.Contains("--speed-test") || args.Contains("--controls-test")) { ApplicationConfiguration.Initialize(); VideoRegression.Run(args.Contains("--speed-test"), args.Contains("--controls-test")); return true; }
        if (args.Contains("--network-test")) {
            int Port() { using var client = new System.Net.Sockets.UdpClient(0); return ((System.Net.IPEndPoint)client.Client.LocalEndPoint!).Port; }
            int aPort = Port(), bPort = Port();
            using var a = new LegacyDatagram($"127.0.0.1:{aPort}", $"127.0.0.1:{bPort}");
            using var b = new LegacyDatagram($"127.0.0.1:{bPort}", $"127.0.0.1:{aPort}");
            var received = new System.Collections.Concurrent.ConcurrentQueue<WireEvent>();
            b.Received += (packet, _) => received.Enqueue(packet); b.Start(); a.Start();
            a.Send("progress", 1000, 60000, 2); a.Send("event", null, null, 1); a.Send("event", 12000, null, null); a.Send("event", null, null, null, 1500);
            using var legacy = new System.Net.Sockets.UdpClient();
            legacy.Send(Encoding.UTF8.GetBytes("{\"type\":\"event\",\"cur\":15000,\"state\":2}"), new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, bPort));
            var watch = Stopwatch.StartNew(); while (received.Count < 5 && watch.ElapsedMilliseconds < 2000) Thread.Sleep(10);
            var packets = received.ToArray();
            if (packets.Length != 5 || !packets.Any(p => p.speed == 1500 && p.cur == null) || !packets.Any(p => p.state == 1 && p.cur == null) || !packets.Any(p => p.cur == 15000 && p.state == 2)) throw new Exception("UDP protocol regression");
            File.WriteAllText("network-test-result.txt", "PASS: progress, state-only event, seek-only event, speed event, original PotSync JSON"); return true;
        }
        if (args.Contains("--self-test")) {
            var d = new PlaybackChangeDetector(); d.Observe(new(1000, 60000, 2, 0));
            if (d.Observe(new(1030, 60000, 2, .03)) != (false, false)) throw new Exception("normal playback");
            if (d.Observe(new(1030, 60000, 1, .06)) != (true, false)) throw new Exception("pause must not seek");
            if (d.Observe(new(10000, 60000, 1, .09)) != (false, true)) throw new Exception("seek");
            if (d.Observe(new(10000, 60000, 1, .12)) != (false, false)) throw new Exception("duplicate seek");
            d.Reset(false);
            if (d.Observe(new(10000, 60000, 1, .15)).seek) throw new Exception("explicit pause must not seek");
            d.Reset(false); d.Observe(new(0, 60000, 2, 0));
            for (int i = 1; i <= 300; i++) {
                if (d.Observe(new((i / 33) * 1000, 60000, 2, i * .03)).seek) throw new Exception("quantized progress must not seek");
            }
            if (!d.Observe(new(30000, 60000, 2, 9.03)).seek) throw new Exception("playing seek");
            d.Reset(false);
            if (d.Observe(new(30000, 60000, 2, 10)).seek) throw new Exception("timeout recovery must not align");
            File.WriteAllText("self-test-result.txt", "PASS: playback, pause, seek, duplicate suppression"); return true;
        }
        return false;
    }
}
