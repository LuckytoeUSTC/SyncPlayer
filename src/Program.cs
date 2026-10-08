using System.Diagnostics;

namespace SyncPlayer;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
#if DIAGNOSTICS
        if (DiagnosticCommands.TryRun(args)) return;
#endif
        ApplicationConfiguration.Initialize();
        bool firstInstance = true;
#if DIAGNOSTICS
        using var instance = args.Contains("--ui-check") ? null : new Mutex(true, InstanceName(), out firstInstance);
#else
        using var instance = new Mutex(true, InstanceName(), out firstInstance);
#endif
        if (!firstInstance) {
            var existing = Process.GetProcessesByName("SyncPlayer").FirstOrDefault(IsOwnWindow);
            if (existing != null) PotPlayer.ActivateApplication(existing.MainWindowHandle);
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportError(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString()));
        try {
#if DIAGNOSTICS
            if (args.Contains("--ui-check") && args.FirstOrDefault(a => a.StartsWith("--language=")) is string languageArg) Localization.Select(languageArg[11..], false);
#endif
            var form = new MainForm();
#if DIAGNOSTICS
            UiPreview.Attach(form, args);
#endif
            Application.Run(form);
        } catch (Exception ex) { ReportError(ex); }
        finally { if (instance != null && firstInstance) instance.ReleaseMutex(); }
    }
    static void ReportError(Exception error)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SyncPlayer-errors.log");
        try { File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{error}\n\n"); } catch { }
        MessageBox.Show(error.Message + "\n\n" + Localization.F("诊断日志：{0}", path), Localization.T("SyncPlayer 错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    static string InstanceName() => @"Local\SyncPlayer-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
    static bool IsOwnWindow(Process process) {
        try { return process.Id != Environment.ProcessId && process.MainWindowHandle != 0 && string.Equals(Path.GetDirectoryName(process.MainModule?.FileName)?.TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

