using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SyncPlayer;

static class LanRegression
{
    public static void Run()
    {
        var report = new List<string>();
        void Check(bool okay, string title) { if (!okay) throw new Exception(title); report.Add("PASS " + title); }
        void Until(Func<bool> ready, string title, int timeout = 3500) {
            var watch = Stopwatch.StartNew(); while (!ready() && watch.ElapsedMilliseconds < timeout) Thread.Sleep(10);
            Check(ready(), title);
        }
        try {
            Check(DeviceIdentity.ShortTag("machine-one") == DeviceIdentity.ShortTag("machine-one") && DeviceIdentity.ShortTag("machine-one") != DeviceIdentity.ShortTag("machine-two"), "short device identity is stable and differs for distinct machines");
            var duplicates = new[] { new DeviceView("id-one", "same", "127.0.0.1:1", [], false, false, false, null, ""), new DeviceView("id-two", "same", "127.0.0.1:2", [], false, false, false, null, "") };
            Check(DeviceIdentity.DisplayName(duplicates[0], duplicates, "own") != DeviceIdentity.DisplayName(duplicates[1], duplicates, "own"), "duplicate device names are visibly disambiguated");
            Check(DeviceIdentity.Validate("  客厅电脑  ") == "客厅电脑", "custom device name normalization");
            try { DeviceIdentity.Validate(" "); throw new Exception("empty name accepted"); } catch (ArgumentException) { report.Add("PASS empty device name is rejected"); }
            Directory.CreateDirectory("diagnostics"); string settings = Path.GetFullPath("diagnostics/device-settings-test.json");
            DeviceIdentity.SaveName("测试设备", settings); Check(DeviceIdentity.LoadName(settings) == "测试设备", "custom device name persists on disk");
            File.WriteAllText(settings, "broken json"); Check(DeviceIdentity.LoadName(settings).StartsWith("PC-"), "corrupt device settings fall back to generated name");
            var sorted = LanService.SortAddresses([new("10.0.0.2", "virtual", false), new("192.168.1.3", "Wi-Fi", true), new("192.168.1.3", "duplicate", false), new("127.0.0.1", "loopback", false), new("0.0.0.0", "invalid", false)]);
            Check(sorted.Length == 2 && sorted[0].Address == "192.168.1.3", "multi-adapter address prioritization and deduplication (simulated)");
            LocalAddress[] simulated = [];
            using var occupied = new UdpClient(0); int port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
            using var offline = new LanService("offline", false, () => simulated, port);
            Check(offline.Port != port && offline.Port > 0, "occupied control port falls back automatically");
            Check(offline.Addresses.Length == 0, "no-network address state (simulated)");
            simulated = [new("192.168.1.9", "Wi-Fi", true)]; offline.RefreshAddresses();
            Check(offline.Addresses.Single() == $"192.168.1.9:{offline.Port}", "network address changes refresh automatically (provider simulation)");
            using var a = new LanService("A", false, () => []); using var b = new LanService("B", false, () => []);
            a.Publish([new("a", "A video")]); b.Publish([new("b", "B video")]);
            var receivedA = new ConcurrentQueue<WireEvent?>(); var receivedB = new ConcurrentQueue<WireEvent?>();
            a.Controlled += (packet, _, _) => receivedA.Enqueue(packet); b.Controlled += (packet, _, _) => receivedB.Enqueue(packet);
            a.Start(); b.Start();
            using var forged = new UdpClient(0);
            void Raw(LanPacket packet, int destination) => forged.Send(JsonSerializer.SerializeToUtf8Bytes(packet), new IPEndPoint(IPAddress.Loopback, destination));
            Raw(new() { Type = "control", Id = "stranger", Name = "stranger", Token = Guid.NewGuid().ToString("N"), Seq = 1, Event = new() { state = 2 }, Targets = [new("b")] }, b.Port);
            Thread.Sleep(100); Check(receivedB.IsEmpty, "unsolicited control is ignored");
            a.Connect($"127.0.0.1:{b.Port}", ControlDirection.Send);
            Until(() => b.Requests.Length == 1, "manual connection works with discovery disabled");
            Check(!b.Devices.Single().Connected && !a.Devices.Any(p => p.Connected), "connection request does not automatically authorize control");
            b.Respond(b.Requests.Single().Nonce, true);
            Until(() => a.Devices.Any(p => p.Connected), "explicit acceptance establishes connection");
            var peerB = a.Devices.Single(); var peerA = b.Devices.Single();
            Check(peerB.CanSend && !peerB.CanReceive && peerA.CanReceive && !peerA.CanSend, "one-way direction is enforced");
            a.SendControl(peerB.Id, new() { cur = 12000, speed = 1500, state = 2 }, [new("b", 250, true)]);
            Until(() => receivedB.Count == 1, "selected remote window receives seek speed state and settings");
            b.SendControl(peerA.Id, new() { state = 1 }, [new("a")]); Thread.Sleep(100);
            Check(receivedA.IsEmpty, "reverse control is blocked for one-way connection");
            Until(() => a.Devices.Single().Rtt.HasValue, "round-trip latency is measured", 4500);
            a.Disconnect(peerB.Id); Until(() => !b.Devices.Single().Connected, "disconnect removes control authority");
            a.Connect($"127.0.0.1:{b.Port}", ControlDirection.Both);
            Until(() => b.Requests.Length == 1, "new direction requires a new confirmation"); b.Respond(b.Requests.Single().Nonce, false);
            Until(() => a.Devices.Single().Status == Localization.T("对方已拒绝"), "rejection is reported");
            a.Connect($"127.0.0.1:{b.Port}", ControlDirection.Both);
            Until(() => b.Requests.Length == 1, "bidirectional request arrives"); b.Respond(b.Requests.Single().Nonce, true);
            Until(() => a.Devices.Single().Connected, "bidirectional request accepted");
            b.SendControl(peerA.Id, new() { state = 1 }, [new("a")]); Until(() => receivedA.Count == 1, "bidirectional reverse control works");
            b.Rename("renamed-B"); Until(() => a.Devices.Single().Name == "renamed-B", "connected device rename is propagated", 4500);
            string token = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N");
            Raw(new() { Type = "request", Id = "test-controller", Name = "test controller", Token = token, Nonce = nonce, Direction = ControlDirection.Send }, b.Port);
            Until(() => b.Requests.Any(r => r.Nonce == nonce), "test session request arrives"); b.Respond(nonce, true);
            var valid = new LanPacket { Type = "control", Id = "test-controller", Name = "test controller", Token = token, Seq = 3, Event = new() { state = 1 }, Targets = [new("b")] };
            Raw(valid, b.Port); Raw(valid, b.Port); Raw(valid with { Seq = 2 }, b.Port); Raw(valid with { Token = Guid.NewGuid().ToString("N"), Seq = 4 }, b.Port);
            Until(() => receivedB.Count == 2, "valid session control arrives"); Thread.Sleep(100);
            Check(receivedB.Count == 2, "duplicate stale and wrong-token packets are ignored");
            Raw(valid with { Seq = 5, Targets = [new("not-published")] }, b.Port); Thread.Sleep(100);
            Check(receivedB.Count == 2, "unpublished remote window is not controlled");
            Check(a.Addresses.Length == 0 && a.Devices.Single().Connected, "manual loopback connection works without discovery or LAN addresses");
            using (var c = new LanService("third-device", false, getAddresses: () => [])) {
                c.Publish([new("c", "third video")]); c.Start(); var receivedC = new ConcurrentQueue<WireEvent>();
                c.Controlled += (operation, _, _) => { if (operation != null) receivedC.Enqueue(operation); };
                a.Connect($"127.0.0.1:{c.Port}", ControlDirection.Send);
                Until(() => c.Requests.Length == 1, "third device receives independent approval request"); c.Respond(c.Requests.Single().Nonce, true);
                Until(() => a.Devices.Count(p => p.Connected) == 2, "three service instances support two simultaneous peer connections");
                var peerC = a.Devices.Single(p => p.Id == c.Id);
                int beforeB = receivedB.Count;
                a.SendControl(peerB.Id, new() { cur = 21000 }, [new("b")]);
                a.SendControl(peerC.Id, new() { cur = 31000 }, [new("c")]);
                Until(() => receivedB.Count == beforeB + 1 && receivedC.Any(p => p.cur == 31000), "each connected device receives its own window operation");
                Check(!receivedC.Any(p => p.cur == 21000), "messages do not leak to another connected device");
                Until(() => a.Devices.Where(p => p.Connected).All(p => p.Rtt.HasValue), "latency is measured separately for every connection", 4500);
                a.Disconnect(peerC.Id);
                Until(() => !c.Devices.Single().Connected, "disconnect applies only to selected device");
                Check(a.Devices.Single(p => p.Id == peerB.Id).Connected, "other device remains connected after one disconnect");
            }
            a.SetEnabled(false);
            Until(() => !b.Devices.Single(p => p.Id == a.Id).Connected, "remote-off disconnects existing peer");
            try { a.Connect($"127.0.0.1:{b.Port}", ControlDirection.Send); throw new Exception("Disabled connection was accepted"); }
            catch (InvalidOperationException) { report.Add("PASS remote-off blocks outgoing connections"); }
            a.SetEnabled(true);
            a.Connect($"127.0.0.1:{b.Port}", ControlDirection.Send);
            Until(() => b.Requests.Length == 1, "remote-on can reconnect and needs fresh approval");
            b.Respond(b.Requests.Single().Nonce, true);
            Until(() => a.Devices.Single(p => p.Id == b.Id).Connected, "re-enabled connection resumes after approval");
            using (var discoveryBlock = new UdpClient(AddressFamily.InterNetwork)) {
                discoveryBlock.Client.ExclusiveAddressUse = true;
                try {
                    discoveryBlock.Client.Bind(new IPEndPoint(IPAddress.Any, LanService.DiscoveryPort));
                    using var fallback = new LanService("discovery-blocked");
                    Check(fallback.DiscoveryStatus == Localization.T("搜索不可用"), "occupied discovery port keeps manual control port usable");
                    Check(fallback.Port > 0, "control port remains available after discovery failure");
                } catch (SocketException) { report.Add("SKIP discovery-port exclusion: another process already owns the port"); }
            }
            using var discoverA = new LanService("discovery-A"); using var discoverB = new LanService("discovery-B");
            discoverA.Start(); discoverB.Start();
            if (discoverA.Addresses.Length > 0) Until(() => discoverA.Devices.Any(p => p.Id == discoverB.Id) && discoverB.Devices.Any(p => p.Id == discoverA.Id), "real local multicast/broadcast discovery", 5000);
            else report.Add("SKIP real discovery: no LAN adapter");
            Check(!discoverA.Devices.Any(p => p.Connected) && !discoverB.Devices.Any(p => p.Connected), "discovery does not establish a control session");
            report.Add("NOT TESTED: two physical computers, router isolation, real adapter removal, Windows firewall prompts");
        } catch (Exception ex) { report.Add("FAIL " + ex); }
        finally { File.WriteAllLines("lan-test-result.txt", report); }
    }
}
