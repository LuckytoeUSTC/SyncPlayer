using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm
{
    public void TestBind(nint primary)
    {
        refreshing = true; master.SelectedIndex = localPlayers.FindIndex(p => p.Handle == primary);
        foreach (var pair in localOptions) pair.Value.Included = pair.Key != primary;
        refreshing = false; RebuildRows(); UpdateBinding(); TestWaitIdle();
    }
    public void TestNavigation() => input.Hint();
    public void TestWaitIdle() { lock (gate) DrainActions(); }
    public void TestAlign() => Align();
    public void TestJump(int ms) { seek.Value = ms / 1000m; Jump(); }
    public void TestState(int state) => Command(state);
    public void TestOffset(nint handle, int offset) { localOptions[handle].Offset = offset; UpdateBinding(false); TestWaitIdle(); Align(); }
    public void TestFollowers(nint[] handles) { foreach (var option in localOptions) option.Value.Included = handles.Contains(option.Key); UpdateBinding(); TestWaitIdle(); }
    public LanService TestNetwork => lan;
    public void TestMute(nint handle, bool muted) => RunControl(() => PotPlayer.Mute(handle, muted));
    public void TestLanguage(string language) => ChangeLanguage(language, false);
    public void TestOffsetText(nint handle, string text) {
        var row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is WindowRow w && w.Handle == handle);
        row.Cells[2].Value = text; TestWaitIdle();
    }
    public int TestOffsetValue(nint handle) => localOptions[handle].Offset;
    public void TestLocalSync(bool value) { localSyncSwitch.Checked = value; TestWaitIdle(); }
    public void TestRemoteConnection(bool value) { ShowConnections(value); TestWaitIdle(); }
    public void TestRemoteFollower(string peer, string window) { remoteOptions[peer + ":" + window] = new() { Included = true }; }
    public void SaveUi(string path, int mode)
    {
        ShowConnections(mode == 1); UpdateUi(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path);
    }
    public void SaveSettingsUi(string path) => ShowSettingsMenu(path);
}
