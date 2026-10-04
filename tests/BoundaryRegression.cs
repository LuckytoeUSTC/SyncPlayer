using System.Diagnostics;

namespace SyncPlayer;

static class BoundaryRegression
{
    public static void Run()
    {
        var players = PotPlayer.List();
        if (players.Count != 2) throw new Exception("Boundary tests need two open test-video windows");
        var saved = players.Select(p => (p.Handle, PlaybackSample: PotPlayer.Read(p.Handle))).ToArray();
        var lines = new List<string>(); nint a = players[0].Handle, b = players[1].Handle;
        using var form = new MainForm(false);
        void Check(bool okay, string name) { lines.Add((okay ? "PASS " : "FAIL ") + name + $" [a={PotPlayer.Read(a, 20484)}/{PotPlayer.Read(a, 20486)}, b={PotPlayer.Read(b, 20484)}/{PotPlayer.Read(b, 20486)}]"); File.WriteAllLines("boundary-test-result.txt", lines); if (!okay) throw new Exception(name); }
        void Wait(Func<bool> ready, string name, int timeout = 12000) {
            var watch = Stopwatch.StartNew(); bool pass = false;
            while (watch.ElapsedMilliseconds < timeout) {
                try { if (ready()) { pass = true; break; } } catch (IOException) { }
                Application.DoEvents(); Thread.Sleep(20);
            }
            Check(pass, name);
        }
        void Pump(int milliseconds) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(20); } }
        try {
            foreach (var p in saved) { PotPlayer.State(p.Handle, 1); PotPlayer.Speed(p.Handle, 1000); }
            form.TestBind(a); form.TestState(1); form.TestWaitIdle(); form.TestJump(0); form.TestWaitIdle();
            int duration = PotPlayer.Read(a, 20482), end = PlaybackTimeline.End(duration);
            Check(PlaybackTimeline.Position(0, -86400000, duration) == 0 && PlaybackTimeline.Position(int.MaxValue, 86400000, duration) == end, "large offsets clamp without overflow");
            form.TestOffset(b, -1500); form.TestWaitIdle();
            Check(PotPlayer.Read(b, 20484) == 0 && PotPlayer.Read(b, 20486) == 1, "paused negative offset holds the first frame");
            form.TestOffsetText(b, " ");
            Check(form.TestOffsetValue(b) == 0 && PotPlayer.Read(b, 20484) == 0, "empty offset commits as zero and updates the actual player");
            form.TestOffset(b, 750); form.TestWaitIdle();
            Check(Math.Abs(PotPlayer.Read(b, 20484) - 750) <= 100, "positive offset at source start is applied");
            form.TestOffset(b, -3000); form.TestWaitIdle(); form.TestState(2); form.TestWaitIdle();
            Check(PotPlayer.Read(a, 20486) == 2 && PotPlayer.Read(b, 20486) == 1 && PotPlayer.Read(b, 20484) == 0, "playing negative offset holds first frame until timeline enters range");
            Wait(() => { var first = PotPlayer.Read(a); var second = PotPlayer.Read(b); return first.State == 2 && second.State == 2 && first.Position > 4200 && Math.Abs(second.Position - first.Position + 3000) <= 100; }, "negative offset resumes automatically and restores 100 ms alignment");
            form.TestState(1); form.TestWaitIdle(); form.TestJump(0); form.TestWaitIdle(); form.TestOffset(b, -500); form.TestWaitIdle(); form.TestState(2); form.TestWaitIdle();
            Wait(() => { var first = PotPlayer.Read(a); var second = PotPlayer.Read(b); return first.State == 2 && second.State == 2 && Math.Abs(second.Position - first.Position + 500) <= 100; }, "short negative offset resumes during decoder startup alignment");
            form.TestState(1); form.TestWaitIdle(); form.TestOffset(b, 2000); form.TestWaitIdle(); form.TestJump(duration - 5000); form.TestWaitIdle(); form.TestState(2); form.TestWaitIdle();
            Wait(() => PotPlayer.Read(b, 20486) == 1 && Math.Abs(PotPlayer.Read(b, 20484) - end) <= 80, "positive offset stops follower before EOF");
            int held = PotPlayer.Read(b, 20484); Pump(250);
            Check(Math.Abs(held - end) <= 100 && PotPlayer.Read(b, 20484) == held, "last frame remains fixed rather than restarting or advancing playlist");
            PotPlayer.Seek(a, 30000);
            Wait(() => { var first = PotPlayer.Read(a); var second = PotPlayer.Read(b); return first.State == 2 && second.State == 2 && first.Position > 30000 && Math.Abs(second.Position - first.Position - 2000) <= 100; }, "backward seek from EOF hold restores offset and playback");
            form.TestState(1); form.TestWaitIdle(); form.TestOffset(b, -3000); form.TestWaitIdle(); form.TestJump(duration + 5000); form.TestWaitIdle();
            Check(Math.Abs(PotPlayer.Read(a, 20484) - end) <= 100 && Math.Abs(PotPlayer.Read(b, 20484) - end + 3000) <= 100, "out-of-range source jump clamps source before applying follower offset");
            form.TestJump(10000); form.TestWaitIdle(); form.TestOffset(b, duration + 5000); form.TestWaitIdle(); form.TestState(2); form.TestWaitIdle();
            Check(PotPlayer.Read(a, 20486) == 2 && PotPlayer.Read(b, 20486) == 1 && Math.Abs(PotPlayer.Read(b, 20484) - end) <= 100, "offset longer than video holds last frame without stopping source");
            form.TestOffset(b, 0); form.TestWaitIdle();
            Wait(() => { var first = PotPlayer.Read(a); var second = PotPlayer.Read(b); return second.State == 2 && Math.Abs(second.Position - first.Position) <= 100; }, "editing offset out of EOF hold resumes follower");
            form.TestState(1); form.TestWaitIdle(); form.TestFollowers([]); form.TestLocalSync(false);
            PotPlayer.Align(a, [b], 1, 0, new Dictionary<nint, int> { [b] = 750 }, -1500, externalSource: true);
            Check(PotPlayer.Read(a, 20484) == 0 && Math.Abs(PotPlayer.Read(b, 20484) - 750) <= 100, "remote first-window clipping does not shift another window's offset");
            form.TestRemoteConnection(true); using var remote = new LanService("boundary controller", false); remote.Start();
            remote.Connect($"127.0.0.1:{form.TestNetwork.Port}", ControlDirection.Send);
            Wait(() => form.TestNetwork.Requests.Length == 1, "boundary remote request");
            form.TestNetwork.Respond(form.TestNetwork.Requests.Single().Nonce, true);
            Wait(() => remote.Devices.Any(p => p.Connected), "boundary remote approval");
            string peer = remote.Devices.Single().Id;
            remote.SendControl(peer, new() { cur = 0, state = 2, speed = 1000 }, [new(a.ToString("X"), -3000), new(b.ToString("X"), 1000)]);
            Wait(() => PotPlayer.Read(a, 20484) == 0 && PotPlayer.Read(a, 20486) == 1 && PotPlayer.Read(b, 20486) == 2, "remote negative follower holds while other follower plays");
            Wait(() => PotPlayer.Read(a, 20486) == 2 && Math.Abs(PotPlayer.Read(b, 20484) - PotPlayer.Read(a, 20484) - 4000) < 500, "remote held follower automatically resumes from its own absolute offset");
            remote.SendControl(peer, new() { cur = 0, state = 2, speed = 1000 }, [new(a.ToString("X"), -6000), new(b.ToString("X"), 1000)]);
            Wait(() => PotPlayer.Read(a, 20484) == 0 && PotPlayer.Read(a, 20486) == 1, "remote re-enters start hold");
            remote.Disconnect(peer); Pump(6500);
            Check(PotPlayer.Read(a, 20486) == 1 && PotPlayer.Read(a, 20484) == 0, "disconnect cancels scheduled boundary resume");
            lines.Add("PASS all boundary scenarios; local limit 100 ms, remote resume limit 500 ms");
        } catch (Exception ex) { lines.Add("ERROR " + ex); lines.AddRange(form.Trace()); }
        finally {
            form.StopEngine();
            foreach (var p in saved) try { PotPlayer.State(p.Handle, 1); PotPlayer.Seek(p.Handle, p.PlaybackSample.Position); PotPlayer.Speed(p.Handle, p.PlaybackSample.Speed); PotPlayer.State(p.Handle, p.PlaybackSample.State); } catch { }
            File.WriteAllLines("boundary-test-result.txt", lines);
        }
    }
}
