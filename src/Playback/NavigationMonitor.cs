using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SyncPlayer;

// Observe navigation intent only; never swallow or replay the user's input.
sealed class NavigationMonitor : IDisposable
{
    delegate nint Hook(int code, nint message, nint data);
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint GetAncestor(nint window, uint flags);
    readonly Hook keyboard;
    readonly Hook mouse;
    readonly nint keyHook, mouseHook;
    long until, version;
    public long Version => Interlocked.Read(ref version);
    public bool NavigationRecent => Stopwatch.GetTimestamp() < Interlocked.Read(ref until);
    public void Hint() { Interlocked.Increment(ref version); Interlocked.Exchange(ref until, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2); }
    public void Consume() => Interlocked.Exchange(ref until, 0);
    public NavigationMonitor(Func<nint> master)
    {
        bool Active() => GetAncestor(GetForegroundWindow(), 3) == master() && master() != 0;
        keyboard = (code, message, data) => {
            if (code >= 0 && (message == 256 || message == 260) && Active()) {
                int key = Marshal.ReadInt32(data);
                if (key is 0x25 or 0x27 or 0x08 or 0x24 or 0x23 or 0x44 or 0x46) Hint();
            }
            return CallNextHookEx(0, code, message, data);
        };
        mouse = (code, message, data) => {
            if (code >= 0 && message == 514 && Active()) Hint();
            return CallNextHookEx(0, code, message, data);
        };
        keyHook = SetWindowsHookEx(13, keyboard, GetModuleHandle(null), 0);
        mouseHook = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
    }
    public void Dispose() { if (keyHook != 0) UnhookWindowsHookEx(keyHook); if (mouseHook != 0) UnhookWindowsHookEx(mouseHook); }
}
