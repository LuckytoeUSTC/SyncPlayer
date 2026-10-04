using System.Diagnostics;

namespace SyncPlayer;

static class VideoRegression
{
    public static void Run(bool speedsOnly = false, bool controlsOnly = false)
    {
        var players = PotPlayer.List();
        if (players.Count < 2) {
            var videos = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "test-video"), "*.mp4");
            string executable = Process.GetProcessesByName("PotPlayerMini64").Select(p => p.MainModule?.FileName).FirstOrDefault(p => p != null)
                ?? @"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe";
            foreach (string video in videos.Where(v => !players.Any(p => p.Title.Contains(Path.GetFileName(v)))).Take(2 - players.Count)) {
                var launch = new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
                launch.ArgumentList.Add(video); launch.ArgumentList.Add("/new"); Process.Start(launch);
                Thread.Sleep(1500);
            }
            players = PotPlayer.List();
        }
        if (players.Count != 2) throw new Exception("实机回归需要两个播放器，已尝试重新打开 test-video 中的视频");
        var original = players.Select(p => (p.Handle, PlaybackSample: PotPlayer.Read(p.Handle))).ToArray();
        foreach (var p in original) PotPlayer.Speed(p.Handle, 1000);
        var lines = new List<string>(); var a = players[0].Handle; var b = players[1].Handle;
        using var form = new MainForm(connectNetwork: false);
        form.TestBind(a);
        int expectedState = 1;
        void Keyboard(int key) {
            try { PotPlayer.PhysicalKey(a, key); }
            catch (IOException ex) { lines.Add("SKIP physical keyboard: " + ex.Message + "; using PotPlayer key API"); form.TestNavigation(); PotPlayer.Key(a, key); }
        }
        void Check(string name, int tolerance = 100, int? position = null, int? speed = null) {
            for (int pump = 0; pump < 45; pump++) { Application.DoEvents(); Thread.Sleep(10); } form.TestWaitIdle();
            PlaybackSample ReadRetry(nint handle) {
                var timeout = Stopwatch.StartNew();
                while (true) { try { return PotPlayer.Read(handle); } catch (IOException) { if (timeout.ElapsedMilliseconds > 600) throw; Thread.Sleep(10); } }
            }
            PlaybackSample first, second; int delta; bool pass; var settle = Stopwatch.StartNew();
            do {
                first = ReadRetry(a); second = ReadRetry(b);
                delta = (int)Math.Abs(first.Position + (first.State == 2 ? (second.At - first.At) * first.Speed : 0) - second.Position);
                pass = first.State == expectedState && second.State == expectedState && first.Speed == second.Speed && delta <= tolerance
                    && (!position.HasValue || Math.Abs(first.Position - position.Value) < 80)
                    && (!speed.HasValue || first.Speed == speed.Value);
                if (!pass) Thread.Sleep(20);
            } while (!pass && settle.ElapsedMilliseconds < 1500);
            lines.Add($"{DateTime.Now:HH:mm:ss.fff} {(pass ? "PASS" : "FAIL")} {name}: a={first.Position}/{first.State}/{first.Speed} b={second.Position}/{second.State}/{second.Speed} delta={delta}ms");
            File.WriteAllLines("video-test-result.txt", lines);
            if (!pass) {
                Thread.Sleep(1500); lines.Add($"late: a={PotPlayer.Read(a, 20484)} b={PotPlayer.Read(b, 20484)}");
                throw new Exception(name + "同步失败");
            }
        }
        try {
            Thread.Sleep(1500);
            if (controlsOnly) {
                bool muteA = PotPlayer.Muted(a), muteB = PotPlayer.Muted(b);
                try {
                    form.TestState(1); form.TestJump(60000); Check("offset setup", position: 60000);
                    void BiasCheck(string name, int bias, bool clipped = false) {
                        Thread.Sleep(550); form.TestWaitIdle();
                        var first = PotPlayer.Read(a); var second = PotPlayer.Read(b);
                        int expected = Math.Clamp(first.Position + bias, 0, second.Duration - 1);
                        bool pass = first.State == second.State && Math.Abs(second.Position - expected) <= 100;
                        lines.Add($"{(pass ? "PASS" : "FAIL")} {name}: master={first.Position} follower={second.Position} expected={expected}");
                        if (!pass) throw new Exception(name);
                    }
                    form.TestOffset(b, 750); BiasCheck("positive offset", 750);
                    PotPlayer.Key(a, 0x25); BiasCheck("backward seek preserves offset", 750);
                    form.TestOffset(b, -1500); BiasCheck("negative offset", -1500);
                    form.TestJump(1000); BiasCheck("offset clips at video start", -1500, true);
                    form.TestJump(30000); BiasCheck("offset survives explicit jump", -1500);
                    int basis = PotPlayer.Read(a, 20484);
                    for (int i = 0; i < 3; i++) { form.TestAlign(); BiasCheck("repeated offset align " + i, -1500); }
                    if (PotPlayer.Read(a, 20484) != basis) throw new Exception("offset caused master drift");
                    form.TestMute(a, false); form.TestMute(b, true); form.TestMute(b, true);
                    if (PotPlayer.Muted(a) || !PotPlayer.Muted(b)) throw new Exception("mute affected another window or toggled");
                    lines.Add("PASS independent follower mute and repeated mute setter");
                    form.TestMute(a, true); form.TestMute(b, false);
                    if (!PotPlayer.Muted(a) || PotPlayer.Muted(b)) throw new Exception("independent source mute");
                    lines.Add("PASS source can be muted independently");
                    form.TestFollowers([]); int oldPosition = PotPlayer.Read(b, 20484);
                    PotPlayer.Key(a, 0x27); Thread.Sleep(650); form.TestWaitIdle();
                    if (PotPlayer.Read(b, 20484) != oldPosition) throw new Exception("unselected local window was controlled");
                    lines.Add("PASS no local followers: unselected window remains unchanged");
                    form.TestMute(a, muteA); form.TestMute(b, muteB);
                    form.TestBind(a); form.TestOffset(b, 0); Check("reset offset");
                    try { PotPlayer.Read((nint)1); throw new Exception("missing window was accepted"); } catch (IOException) { lines.Add("PASS missing window query fails without hanging"); }
                    form.TestFollowers([]);
                    form.TestRemoteConnection(true);
                    using var remote = new LanService("test controller", false);
                    remote.Publish([new LanWindow("test-remote", "remote video")]);
                    var sentOperations = new System.Collections.Concurrent.ConcurrentQueue<WireEvent>();
                    remote.Controlled += (operation, _, _) => { if (operation != null) sentOperations.Enqueue(operation); };
                    remote.Start(); remote.Connect($"127.0.0.1:{form.TestNetwork.Port}", ControlDirection.Both);
                    void Wait(Func<bool> ready, string scenario) {
                        var watch = Stopwatch.StartNew(); bool complete = false;
                        while (watch.ElapsedMilliseconds < 3500) {
                            try { if (ready()) { complete = true; break; } } catch (IOException) { }
                            Application.DoEvents(); Thread.Sleep(15);
                        }
                        if (!complete) throw new Exception(scenario);
                    }
                    Wait(() => form.TestNetwork.Requests.Length == 1, "remote video request");
                    form.TestNetwork.Respond(form.TestNetwork.Requests.Single().Nonce, true);
                    Wait(() => remote.Devices.Any(d => d.Connected), "remote video handshake");
                    form.TestFollowers([b]); form.TestLocalSync(false);
                    form.TestRemoteFollower(form.TestNetwork.Devices.Single().Id, "test-remote");
                    int unchanged = PotPlayer.Read(b, 20484);
                    PotPlayer.Seek(a, 20000);
                    Wait(() => sentOperations.Any(e => e.cur.HasValue && Math.Abs(e.cur.Value - 20000) < 80), "remote propagation with local sync off");
                    if (PotPlayer.Read(b, 20484) != unchanged) throw new Exception("local sync off still controlled follower");
                    lines.Add("PASS local sync off preserves local follower and still sends selected remote window events");
                    string peer = remote.Devices.Single().Id, window = b.ToString("X");
                    int sourcePosition = PotPlayer.Read(a, 20484);
                    remote.SendControl(peer, new() { cur = 45000, state = 1, speed = 1100 }, [new(window, 250, true)]);
                    Wait(() => Math.Abs(PotPlayer.Read(b, 20484) - 45250) < 80 && PotPlayer.Muted(b), "remote absolute seek offset mute");
                    form.TestWaitIdle();
                    if (PotPlayer.Read(a, 20484) != sourcePosition || PotPlayer.Read(b, 20501) != 1100) throw new Exception("remote selected wrong window or speed");
                    lines.Add("PASS remote protocol controls only selected real window, with offset speed and mute");
                    remote.SendControl(peer, null, [new(window, 0, false)]);
                    Wait(() => !PotPlayer.Muted(b), "remote unmute");
                    if (PotPlayer.Read(b, 20484) != 45250) throw new Exception("remote mute moved playback");
                    lines.Add("PASS remote mute setting does not move playback");
                    remote.SendControl(peer, new() { speed = 900 }, [new(window)]);
                    Wait(() => PotPlayer.Read(b, 20501) == 900, "remote speed");
                    if (PotPlayer.Read(b, 20484) != 45250) throw new Exception("remote speed sought unexpectedly");
                    lines.Add("PASS remote speed update does not seek");
                    remote.SendControl(peer, new() { state = 2 }, [new(window)]);
                    Wait(() => PotPlayer.Read(b, 20486) == 2, "remote play");
                    remote.SendControl(peer, new() { state = 1 }, [new(window)]);
                    Wait(() => PotPlayer.Read(b, 20486) == 1, "remote pause");
                    lines.Add("PASS remote play and pause states");
                    remote.Disconnect(peer); Thread.Sleep(250); form.TestWaitIdle(); int disconnected = PotPlayer.Read(b, 20484);
                    remote.SendControl(peer, new() { cur = 12000 }, [new(window)]); Thread.Sleep(150);
                    if (Math.Abs(PotPlayer.Read(b, 20484) - disconnected) > 100) throw new Exception("disconnected remote changed playback");
                    lines.Add("PASS remote disconnect prevents real window control");
                    lines.Add("PASS offset and mute scenarios"); return;
                } finally { try { PotPlayer.Mute(a, muteA); PotPlayer.Mute(b, muteB); } catch { } }
            }
            if (speedsOnly) {
                form.TestState(1); form.TestJump(50000); Check("speed setup", position: 50000);
                expectedState = 2; form.TestState(2); Check("speed setup play");
                int seeks = form.AutomaticSeeks;
                PotPlayer.Speed(a, 2000); Check("direct 2x", 100);
                Thread.Sleep(2000); Check("direct 2x sustained", 100);
                PotPlayer.Speed(a, 700); Check("direct 0.7x", 100);
                if (form.AutomaticSeeks != seeks) throw new Exception("speed change caused a seek");
                lines.Add("PASS speed events synchronize rate without seeking"); return;
            }
            form.TestState(1); form.TestJump(60000); Check("absolute seek paused", position: 60000);
            int before = PotPlayer.Read(a, 20484);
            for (int i = 0; i < 5; i++) { form.TestAlign(); Check("repeat align " + i); }
            if (PotPlayer.Read(a, 20484) != before) throw new Exception("master drift");
            foreach (int key in new[] { 0x27, 0x25, 0x200 | 0x27, 0x200 | 0x25, 0x25, 0x25 }) {
                PotPlayer.Key(a, key); Check($"paused key 0x{key:X}");
            }
            foreach (int target in new[] { 42000, 51000, 38000, 47000 }) { PotPlayer.Seek(a, target); Thread.Sleep(90); }
            Check("rapid paused seek burst latest target", position: 47000);
            foreach (int key in new[] { 0x27, 0x25, 0x227, 0x225 }) { Keyboard(key); Check($"keyboard paused key 0x{key:X}"); }
            PotPlayer.Seek(a, 45000); Check("seekbar equivalent absolute backward", position: 45000);
            int[] speeds = { 1100, 1200, 1100, 1000 }; int j = 0;
            foreach (int key in new[] { 0x43, 0x43, 0x58, 0x5A }) { PotPlayer.Key(a, key); Check($"speed key 0x{key:X}", speed: speeds[j++]); }
            expectedState = 2; form.TestState(2); Thread.Sleep(1200); Check("play", 100);
            foreach (int key in new[] { 0x27, 0x25, 0x200 | 0x27, 0x200 | 0x25 }) { Keyboard(key); Check($"keyboard playing key 0x{key:X}", 100); }
            PotPlayer.Speed(a, 2000); Check("2x", 100);
            int seeksBefore = form.AutomaticSeeks;
            for (int tick = 0; tick < 3; tick++) { Thread.Sleep(200); Check("2x sustained no step seek " + tick, 100); }
            if (form.AutomaticSeeks != seeksBefore) throw new Exception("steady playback triggered periodic seek");
            PotPlayer.Speed(a, 700); Check("0.7x", 100);
            PotPlayer.Seek(a, 30000); Check("playing seekbar equivalent backward", 100);
            expectedState = 1; form.TestState(1); Check("pause", 150);
            form.TestAlign(); Check("mixed operations final align");
            lines.Add("PASS all scenarios; master did not drift during repeated paused align");
        } catch (Exception ex) { lines.Add("ERROR " + ex); lines.AddRange(form.Trace()); }
        finally {
            form.StopEngine();
            foreach (var p in original) { try { PotPlayer.Speed(p.Handle, p.PlaybackSample.Speed); PotPlayer.Seek(p.Handle, p.PlaybackSample.Position); PotPlayer.State(p.Handle, p.PlaybackSample.State); } catch { } }
            File.WriteAllLines("video-test-result.txt", lines);
        }
    }
}
