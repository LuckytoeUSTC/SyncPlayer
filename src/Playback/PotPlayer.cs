using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SyncPlayer;

static class PotPlayer
{
    delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(nint hwnd, uint msg, nuint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SendMessageTimeout(nint hwnd, uint msg, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    public static List<PlayerWindow> List()
    {
        var list = new List<PlayerWindow>();
        EnumWindows((h, _) => {
            var cls = new StringBuilder(256); GetClassName(h, cls, cls.Capacity);
            if (IsWindowVisible(h) && cls.ToString().StartsWith("PotPlayer", StringComparison.OrdinalIgnoreCase)) {
                var title = new StringBuilder(2048); GetWindowText(h, title, title.Capacity);
                list.Add(new PlayerWindow(h, title.ToString()));
            }
            return true;
        }, 0);
        return list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderBy(p => (long)p.Handle).Select((p, i) => p with { DisplayName = group.Count() > 1 ? $"{p.Name} ({i + 1})" : p.Name })).ToList();
    }
    public static int Read(nint h, uint command)
    {
        if (SendMessageTimeout(h, 1024, command, 0, 2 | 32, 40, out var result) == 0)
            throw new IOException(Localization.F("窗口 {0} 未响应或已关闭", $"0x{h:X}"));
        return unchecked((int)result);
    }
    public static PlaybackSample Read(nint h)
    {
        int state = Read(h, 20486), speed = Read(h, 20501), duration = Read(h, 20482);
        double before = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        int position = Read(h, 20484);
        double after = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        return new(position, duration, state, (before + after) / 2, speed);
    }
    public static void State(nint h, int state)
    {
        if (state == 1 && Read(h, 20486) is -1 or 0) {
            Command(h, 20001);
            WaitFor(() => Read(h, 20486) == 2, Localization.T("播放器未能加载视频"));
        }
        if (state is 1 or 2) Set(h, 20487, state);
        else Send(h, 273, 20002, 0);
    }
    static void Set(nint h, uint command, int value)
    {
        for (int attempt = 0; attempt < 2; attempt++) {
            if (SendMessageTimeout(h, 1024, command, value, 2 | 32, 400, out _) != 0) return;
            // Explicit setters are safe to repeat; a decoder can finish after timeout.
            uint query = command switch { 20487 => 20486, 20502 => 20501, 20498 => 20497, _ => 0 };
            if (query != 0) { try { if (Read(h, query) == value) return; } catch (IOException) { } }
        }
        throw new IOException(Localization.F("窗口 {0} 命令未确认", $"0x{h:X}"));
    }
    public static void Seek(nint h, int ms)
    {
        if (SendMessageTimeout(h, 1024, 20485, ms, 2 | 32, 400, out _) == 0)
            throw new IOException(Localization.F("窗口 {0} 跳转未确认", $"0x{h:X}"));
    }
    public static void Command(nint h, uint command) => Send(h, 273, command, 0);
    public static void Speed(nint h, int speed) => Set(h, 20502, speed);
    public static bool Muted(nint h) => Read(h, 20497) == 1;
    public static void Mute(nint h, bool muted) => Set(h, 20498, muted ? 1 : 0);
    public static void Key(nint h, int key) => Set(h, 20496, key);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int command);
    public static void ActivateApplication(nint h) { ShowWindow(h, 9); SetForegroundWindow(h); }
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, out uint process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    public static void PhysicalKey(nint h, int key)
    {
        uint current = GetCurrentThreadId(), foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        if (current != foreground) AttachThreadInput(current, foreground, true);
        try { SetForegroundWindow(h); }
        finally { if (current != foreground) AttachThreadInput(current, foreground, false); }
        if (GetForegroundWindow() != h) throw new IOException(Localization.T("测试无法激活播放器"));
        bool control = (key & 0x200) != 0;
        if (control) keybd_event(0x11, 0, 0, 0);
        keybd_event((byte)key, 0, 0, 0); keybd_event((byte)key, 0, 2, 0);
        if (control) keybd_event(0x11, 0, 2, 0);
        Application.DoEvents();
    }
    static readonly HashSet<nint> prepared = new();
    static readonly HashSet<nint> changedKeyframe = new();
    public static void Prepare(nint h)
    {
        if (prepared.Contains(h)) return;
        var snapshot = Read(h);
        if (snapshot.Duration < 1500) return;
        try {
            State(h, 1); WaitFor(() => Read(h, 20486) == 1, Localization.T("播放器未能暂停"));
            int original = Read(h, 20484);
            int target = Math.Clamp(snapshot.Duration / 2 + 137, 500, snapshot.Duration - 500);
            Seek(h, target); Thread.Sleep(350);
            WaitFor(() => { int value = Read(h, 20484); return Math.Abs(value - target) < 100 || value != original; }, Localization.T("跳转未响应"));
            if (Math.Abs(Read(h, 20484) - target) > 100) {
                Command(h, 10265); changedKeyframe.Add(h);
                Seek(h, target); Thread.Sleep(350);
                WaitFor(() => Math.Abs(Read(h, 20484) - target) <= 80, Localization.T("播放器未能精确定位"));
            }
            prepared.Add(h);
            Seek(h, original);
        } finally { State(h, snapshot.State); }
    }
    public static void RestorePreferences()
    {
        foreach (var h in changedKeyframe) { try { Command(h, 10265); } catch { } }
        changedKeyframe.Clear(); prepared.Clear();
    }
    public static void Align(nint master, IEnumerable<nint> followers, int? desiredState = null, int? desiredPosition = null, IReadOnlyDictionary<nint, int>? offsets = null, int masterOffset = 0, bool externalSource = false)
    {
        var snapshot = Read(master);
        if (snapshot.Duration <= 0) throw new IOException(Localization.T("主窗口尚未加载视频"));
        var windows = new[] { master }.Concat(followers).Distinct().ToArray();
        var durations = windows.ToDictionary(h => h, h => Read(h, 20482));
        int Offset(nint h) => h == master ? masterOffset : offsets?.GetValueOrDefault(h) ?? 0;
        int Position(nint h, int basis) => PlaybackTimeline.Position(basis, Offset(h), durations[h]);
        // Every window receives the same ordered sequence; explicit state setters
        // are idempotent, unlike play/pause toggle commands.
        Parallel.ForEach(windows, h => State(h, 1));
        WaitFor(() => windows.All(h => Read(h, 20486) == 1), Localization.T("播放器未能暂停，对齐已停止"));
        Thread.Sleep(180);
        int position = desiredPosition ?? Read(master, 20484) - masterOffset;
        if (desiredPosition.HasValue && !externalSource) position = PlaybackTimeline.Position(position, 0, durations[master]);
        // Followers seek to the master timestamp; an explicit jump also moves the master.
        Parallel.ForEach(windows, h => { Speed(h, snapshot.Speed); if (h != master || desiredPosition.HasValue) Seek(h, Position(h, position)); });
        Thread.Sleep(200);
        var last = new Dictionary<nint, int>();
        int stable = 0;
        WaitFor(() => {
            var positions = windows.ToDictionary(h => h, h => Read(h, 20484));
            if (!desiredPosition.HasValue && Math.Abs(positions[master] - Position(master, position)) > 80) {
                position = positions[master] - masterOffset;
                foreach (var h in windows.Where(h => h != master)) Seek(h, Position(h, position));
                stable = 0; last.Clear(); return false;
            }
            bool ready = positions.All(p => Math.Abs(p.Value - Position(p.Key, position)) <= 80)
                && positions.All(p => last.TryGetValue(p.Key, out int old) && old == p.Value);
            last = positions; stable = ready ? stable + 1 : 0;
            return stable >= 3;
        }, Localization.T("跳转未稳定，窗口保持暂停，请重试对齐"));
        Together(windows, h => State(h, PlaybackTimeline.State(position, Offset(h), durations[h], desiredState ?? snapshot.State)));
        if ((desiredState ?? snapshot.State) == 2 && windows.Length > 1) {
            var sides = windows.Where(h => h != master).ToDictionary(h => h, h => PlaybackTimeline.Boundary(position, Offset(h), durations[h]));
            void EnforceBoundary(PlaybackSample source) {
                if (source.State != 2) return;
                foreach (var h in windows.Where(h => h != master)) {
                    int side = PlaybackTimeline.Boundary(source.Position - masterOffset, Offset(h), durations[h]);
                    if (side == sides[h]) continue;
                    sides[h] = side; Hold(h, Position(h, source.Position - masterOffset));
                    if (side == 0) State(h, 2);
                }
            }
            // Correct decoder startup latency once within this operation, without seeking.
            var startup = Stopwatch.StartNew();
            while (startup.ElapsedMilliseconds < 1500) {
                var current = Read(master); if (current.State != 2) break;
                EnforceBoundary(current); Thread.Sleep(20);
            }
            int settled = 0;
            for (int attempt = 0; attempt < 6; attempt++) {
                bool close = true;
                var primary = Read(master);
                if (primary.State != 2) break;
                EnforceBoundary(primary);
                foreach (var h in windows.Where(h => h != master)) {
                    if (PlaybackTimeline.Boundary(primary.Position - masterOffset, Offset(h), durations[h]) != 0) continue;
                    var other = Read(h);
                    double delta = other.Position - Position(h, primary.Position - masterOffset) - (other.At - primary.At) * primary.Speed;
                    if (Math.Abs(delta) <= 60 || Math.Abs(delta) > 1500) continue;
                    close = false;
                    var leading = delta > 0 ? h : master;
                    Speed(leading, Math.Max(200, primary.Speed * 9 / 10));
                    try {
                        var settling = Stopwatch.StartNew();
                        while (settling.ElapsedMilliseconds < 2500) {
                            Thread.Sleep(20);
                            var current = Read(master); var following = Read(h);
                            EnforceBoundary(current);
                            if (PlaybackTimeline.Boundary(current.Position - masterOffset, Offset(h), durations[h]) != 0) break;
                            double gap = following.Position - Position(h, current.Position - masterOffset) - (following.At - current.At) * current.Speed;
                            if (delta > 0 ? gap <= 15 : gap >= -15) break;
                        }
                    } finally { Speed(leading, primary.Speed); }
                }
                settled = close ? settled + 1 : 0;
                if (settled >= 3) break;
                Thread.Sleep(200);
            }
        }
    }
    public static void SettleRate(nint master, IEnumerable<nint> followers, IReadOnlyDictionary<nint, int> offsets, int speed)
    {
        Thread.Sleep(350);
        foreach (var h in followers) {
            var source = Read(master); var follower = Read(h); int offset = offsets.GetValueOrDefault(h);
            if (source.State != 2 || source.Speed != speed || PlaybackTimeline.Boundary(source.Position, offset, follower.Duration) != 0) continue;
            double Gap(PlaybackSample a, PlaybackSample b) => b.Position - (a.Position + offset) - (b.At - a.At) * a.Speed;
            double gap = Gap(source, follower);
            if (Math.Abs(gap) <= 75 || Math.Abs(gap) > 1000) continue;
            // One correction per speed change, using only the follower's rate.
            Speed(h, Math.Max(200, speed * (gap > 0 ? 9 : 11) / 10));
            try {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 1600) {
                    Thread.Sleep(30); source = Read(master); follower = Read(h);
                    double current = Gap(source, follower);
                    if (source.State != 2 || source.Speed != speed || Math.Abs(current) > 1000 || PlaybackTimeline.Boundary(source.Position, offset, follower.Duration) != 0 || (gap > 0 ? current <= 25 : current >= -25)) break;
                }
            } finally { Speed(h, Read(master, 20501)); }
        }
    }
    public static void Hold(nint h, int position)
    {
        State(h, 1); Seek(h, position);
        int previous = -1, stable = 0;
        WaitFor(() => {
            int current = Read(h, 20484);
            stable = current == previous && Math.Abs(current - position) <= 80 && Read(h, 20486) == 1 ? stable + 1 : 0;
            previous = current; return stable >= 2;
        }, Localization.T("播放器未能精确定位"));
    }
    static void Together(nint[] windows, Action<nint> action)
    {
        using var ready = new CountdownEvent(windows.Length);
        using var start = new ManualResetEventSlim();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var threads = windows.Select(h => new Thread(() => {
            ready.Signal(); start.Wait();
            try { action(h); } catch (Exception ex) { errors.Enqueue(ex); }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        ready.Wait(); start.Set();
        foreach (var thread in threads) thread.Join();
        if (errors.TryDequeue(out var error)) throw error;
    }
    static void WaitFor(Func<bool> ready, string failure)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 4000) {
            try { if (ready()) return; } catch (IOException) { }
            Thread.Sleep(20);
        }
        throw new IOException(failure);
    }
    static void Send(nint h, uint message, uint command, int value)
    {
        if (!PostMessage(h, message, command, value)) throw new IOException(Localization.F("无法控制窗口 {0}，请检查权限或刷新列表", $"0x{h:X}"));
    }
}
