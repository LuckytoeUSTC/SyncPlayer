using System.Diagnostics;

namespace SyncPlayer;

static class OffsetRegression
{
    public static void Run()
    {
        var report = new List<string>();
        var players = PotPlayer.List();
        if (players.Count < 2) throw new Exception("Two player windows are required");
        var originals = players.Select(p => (p.Handle, Sample: PotPlayer.Read(p.Handle))).ToArray();
        nint a = players[0].Handle, b = players[1].Handle;
        using var form = new MainForm(false);
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); report.Add("PASS " + name); }
        void Pump() { for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(10); } form.TestWaitIdle(); }
        try {
            form.Show(); form.TestBind(a); form.TestState(1); form.TestJump(60000); Pump();
            var controls = Descendants(form); var grid = controls.OfType<DataGridView>().Single();
            var playback = controls.OfType<IconButton>().Single(c => c.Symbol is "play" or "pause");
            var row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Cells[4].Value?.ToString() == Localization.T("主窗口"));
            var primary = row.Cells[2];
            primary.Value = "—"; primary.ReadOnly = true; grid.CurrentCell = primary;
            playback.Focus(); Pump();
            Check(primary.ErrorText.Length == 0 && (playback.Focused || form.ActiveControl == playback), $"read-only nonnumeric cell cannot trap focus (error='{primary.ErrorText}', active={form.ActiveControl?.GetType().Name})");
            primary.ReadOnly = false; primary.Value = "0.0"; Pump();
            grid.CurrentCell = primary; Check(grid.BeginEdit(false), "primary offset is editable");
            var editor = (OffsetEditor)grid.EditingControl!; editor.RawText = "0.1"; editor.Submit(); Pump();
            Check(!grid.IsCurrentCellInEditMode && !editor.ContainsFocus, "Enter commits offset and leaves editor");
            Check(Math.Abs(PotPlayer.Read(a, 20484) - 60100) <= 100 && Math.Abs(PotPlayer.Read(b, 20484) - 60000) <= 100, "primary 0.1s offset moves primary and preserves follower position");
            grid.CurrentCell = primary; grid.BeginEdit(false); editor = (OffsetEditor)grid.EditingControl!;
            editor.RawText = "0.2"; playback.Focus(); Pump();
            Check(!grid.IsCurrentCellInEditMode && !editor.ContainsFocus && (playback.Focused || form.ActiveControl == playback), "clicking outside commits offset without stealing focus back");
            Check(form.TestOffsetValue(a) == 200, "focus-loss commits the actual offset value");
            form.TestOffsetText(a, ""); Pump();
            Check(form.TestOffsetValue(a) == 0 && Math.Abs(PotPlayer.Read(a, 20484) - 60000) <= 100, "blank primary offset resets to zero without cumulative drift");
            var follower = grid.Rows.Cast<DataGridViewRow>().Single(r => !r.ReadOnly && r != row);
            grid.CurrentCell = follower.Cells[2]; grid.BeginEdit(false);
            editor = (OffsetEditor)grid.EditingControl!;
            editor.RawText = "0.7"; Application.DoEvents();
            var expected = TimeInput.FieldBounds(grid.GetCellDisplayRectangle(2, follower.Index, false), grid.Font);
            Check(grid.EditingPanel.Bounds == expected && editor.Height == expected.Height && editor.Controls.Cast<Control>().All(c => c.Top >= 0 && c.Bottom <= editor.ClientSize.Height), "focused composite offset editor fits wrapped row without clipping");
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("diagnostics/offset-focused.png"); }
            editor.Submit(); Pump();
            form.TestOffsetText(a, "0.3"); Pump();
            form.TestRemoteFollower("offline-peer", "offline-window");
            form.TestRemoteOffset("offline-peer", "offline-window", 700);
            int sourceBeforeReset = PotPlayer.Read(a, 20484);
            var reset = Descendants(form).OfType<Button>().Single(c => c.Text == Localization.T("取消偏移"));
            reset.PerformClick(); Pump();
            Check(form.TestOffsetValue(a) == 0 && form.TestOffsetValue(b) == 0 && form.TestRemoteOffsetValue("offline-peer", "offline-window") == 0, "reset clears primary, follower and hidden remote offsets");
            bool resetSynced = false; var settle = Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < 1500 && !resetSynced) {
                try { int first = PotPlayer.Read(a, 20484), second = PotPlayer.Read(b, 20484); resetSynced = Math.Abs(first - sourceBeforeReset) <= 100 && Math.Abs(first - second) <= 100; } catch (IOException) { }
                if (!resetSynced) { Application.DoEvents(); Thread.Sleep(20); }
            }
            Check(resetSynced, "reset synchronizes without moving the source back");
            row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Cells[4].Value?.ToString() == Localization.T("主窗口")); primary = row.Cells[2];
            var input = controls.OfType<TimeInput>().First(c => c is not OffsetEditor);
            input.Value = 0; input.Step(1); Check(input.Value == .1m, "seek arrow increment is 0.1s");
            input.Step(-1); Check(input.Value == 0, "seek arrow decrements by 0.1s and clamps at start");
            grid.CurrentCell = primary; grid.BeginEdit(false); editor = (OffsetEditor)grid.EditingControl!;
            editor.RawText = "invalid";
            var watch = Stopwatch.StartNew(); form.Close();
            Check(form.IsDisposed && watch.ElapsedMilliseconds < 1000, $"window closes with invalid pending offset (disposed={form.IsDisposed}, elapsed={watch.ElapsedMilliseconds}ms)");
        } catch (Exception error) { report.Add("FAIL " + error); }
        finally {
            form.StopEngine();
            foreach (var p in originals) { PotPlayer.Speed(p.Handle, p.Sample.Speed); PotPlayer.Seek(p.Handle, p.Sample.Position); PotPlayer.State(p.Handle, p.Sample.State); }
            File.WriteAllLines("offset-test-result.txt", report);
        }
    }
    static Control[] Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray();
}
