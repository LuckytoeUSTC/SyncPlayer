using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm
{
    static Label Label(string text) => new() { Text = Localization.T(text), AutoSize = true, Margin = new Padding(0, 7, 12, 7) };
    static RadioButton ModeChoice(string text, bool selected) {
        var choice = new RadioButton { Text = Localization.T(text), Checked = selected, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty, Padding = Padding.Empty, AutoCheck = false };
        choice.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
        choice.FlatAppearance.CheckedBackColor = Color.FromArgb(224, 230, 238);
        return choice;
    }
    IconButton MakeIcon(string symbol, string label, Action action, bool subtle = false)
    {
        var button = new IconButton { Symbol = symbol, Tag = label, AccessibleName = Localization.T(label) };
        if (subtle) { button.FlatAppearance.BorderSize = 0; button.BackColor = BackColor; }
        tooltips.SetToolTip(button, Localization.T(label));
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SyncPlayer"); } };
        return button;
    }
    void UpdateIconLabels()
    {
        void Walk(Control root) {
            if (root is IconButton icon && icon.Tag is string key) { icon.AccessibleName = Localization.T(key); tooltips.SetToolTip(icon, Localization.T(key)); }
            foreach (Control child in root.Controls) Walk(child);
        }
        Walk(this);
        seek.UpdateAccessibleLabels();
        if (requestMasterButton != null) tooltips.SetToolTip(requestMasterButton, Localization.T("申请主控，需对方接受"));
    }
    void FitHeaders()
    {
        foreach (DataGridViewColumn column in grid.Columns)
            column.MinimumWidth = Math.Max(column.Name == "title" ? 180 : 0, TextRenderer.MeasureText(column.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Width + 32);
    }
    void AlignControlRows()
    {
        int padding = (int)Math.Ceiling(12 * DeviceDpi / 96.0);
        int height = Math.Max(Font.Height, TextRenderer.MeasureText("Agjpqy", Font).Height) + padding;
        master.ItemHeight = height - 8;
        height = Math.Max(height, master.PreferredHeight);
        void Walk(Control root) {
            foreach (Control child in root.Controls) {
                if (child is Button button) {
                    button.AutoSize = false;
                    button.Padding = Padding.Empty; button.TextAlign = ContentAlignment.MiddleCenter;
                    button.Size = new Size(button is IconButton ? height : TextRenderer.MeasureText(button.Text, button.Font).Width + 24, height);
                    button.Margin = new Padding(0, 0, 8, 0); button.Anchor = AnchorStyles.Left;
                } else if (child is ComboBox or NumericUpDown or TextBox) {
                    int top = Math.Max(0, (height - child.PreferredSize.Height) / 2);
                    child.Margin = new Padding(0, top, 8, top); child.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                } else if (child is RadioButton choice) {
                    choice.Size = new Size(TextRenderer.MeasureText(choice.Text, choice.Font).Width + 28, height);
                    choice.Margin = Padding.Empty; choice.BackColor = Color.White;
                } else if (child is TimeInput) {
                    child.Height = height; child.Margin = new Padding(0, 0, 8, 0);
                } else if (child is Label label && label != status && label != connectedDevice) {
                    label.Anchor = AnchorStyles.Left;
                    label.Margin = new Padding(0, root is TableLayoutPanel ? 0 : Math.Max(0, (height - label.PreferredHeight) / 2), 12, 0);
                }
                if (child is not TimeInput) Walk(child);
            }
        }
        Walk(this); PerformLayout();
    }
    Button Button(string text, Action action, bool discardEdit = false)
    {
        var button = new Button { Text = Localization.T(text), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 3, 8, 3) };
        button.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
        button.Click += (_, _) => { try { if (discardEdit) grid.CancelEdit(); else if (!grid.EndEdit()) return; action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SyncPlayer", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
        return button;
    }
    void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
        for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(root);
        var source = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Margin = new Padding(0, 0, 0, 12) };
        source.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 2; i < 5; i++) source.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        source.Controls.Add(Label(Localization.T("主窗口")), 0, 0); source.Controls.Add(master, 1, 0);
        source.Controls.Add(MakeIcon("refresh", "刷新", RefreshPlayers), 2, 0);
        source.Controls.Add(MakeIcon("help", "说明", OpenManual, true), 3, 0);
        source.Controls.Add(MakeIcon("settings", "语言", ShowSettings, true), 4, 0);
        root.Controls.Add(source, 0, 0);
        var switches = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        switches.Controls.Add(localSyncSwitch); switches.Controls.Add(remoteConnectionSwitch); root.Controls.Add(switches, 0, 1);
        BuildConnectionPanel(); root.Controls.Add(connectionPanel, 0, 2);
        BuildGrid(); root.Controls.Add(grid, 0, 3);
        var playback = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
        playbackButton.Click += (_, _) => Command((requestedPlaybackState >= 0 ? requestedPlaybackState : playbackState) == 2 ? 1 : 2); playback.Controls.Add(playbackButton);
        playback.Controls.Add(syncButton = Button(Localization.T("同步"), Align));
        playback.Controls.Add(resetOffsetsButton = Button(Localization.T("取消偏移"), ResetOffsets, discardEdit: true));
        playback.Controls.Add(details); root.Controls.Add(playback, 0, 4);
        var jump = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        jump.Controls.Add(jumpButton = Button(Localization.T("跳转"), Jump)); jump.Controls.Add(seek); jump.Controls.Add(Label(Localization.T("秒"))); root.Controls.Add(jump, 0, 5);
        seek.CommitRequested += (_, _) => { try { seek.Value = seek.Value; playbackButton.Focus(); } catch (FormatException) { message = Localization.T("请检查输入的秒数"); } };
        root.Controls.Add(status, 0, 6);
        details.ForeColor = Color.FromArgb(90, 98, 110); status.ForeColor = details.ForeColor;
        root.SizeChanged += (_, _) => { details.MaximumSize = new Size(Math.Max(160, root.ClientSize.Width - 40), 0); status.MaximumSize = details.MaximumSize; };
        master.SelectedIndexChanged += (_, _) => { if (!refreshing) { RebuildRows(); UpdateBinding(); } };
        master.DropDown += (_, _) => master.DropDownWidth = Math.Min(Screen.FromControl(this).WorkingArea.Width - 40, Math.Max(master.Width, localPlayers.Select(p => TextRenderer.MeasureText(p.ToString(), master.Font).Width + 32).DefaultIfEmpty().Max()));
        localSyncSwitch.Click += (_, _) => SetSyncMode(SyncMode.Local);
        remoteConnectionSwitch.Click += (_, _) => SetSyncMode(SyncMode.Remote);
        localSyncSwitch.CheckedChanged += (_, _) => { if(localSyncSwitch.Checked)SetSyncMode(SyncMode.Local); };
        remoteConnectionSwitch.CheckedChanged += (_, _) => { if(remoteConnectionSwitch.Checked)SetSyncMode(SyncMode.Remote); };
    }
    void BuildConnectionPanel()
    {
        var box = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 10) };
        connectionPanel.Controls.Add(box);
        deviceName.Text = lan.Name;
        var nameRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        nameRow.Controls.Add(Label(Localization.T("本机名称"))); nameRow.Controls.Add(deviceName); box.Controls.Add(nameRow);
        deviceName.Leave += (_, _) => SaveDeviceName();
        deviceName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SaveDeviceName(); e.SuppressKeyPress = true; } };
        var addressRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        addressRow.Controls.Add(Label("连接码")); addressRow.Controls.Add(localAddress);
        addressRow.Controls.Add(Button("复制连接码", () => { if (localAddress.TextLength > 0) Clipboard.SetText(localAddress.Text); else message = Localization.T("尚未创建连接码"); }));
        box.Controls.Add(addressRow);
        var destinations = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        destinations.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); destinations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        destinations.Controls.Add(Label("主控连接码"), 0, 0); destinations.Controls.Add(manualAddress, 1, 0);
        box.Controls.Add(destinations);
        var actionsRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        actionsRow.Controls.Add(Button("发起", () => lan.Create())); actionsRow.Controls.Add(Button("加入", Connect));
        actionsRow.Controls.Add(Button(Localization.T("断开"), () => { lan.Disconnect(); }));
        actionsRow.Controls.Add(requestMasterButton = Button("接管", () => lan.RequestMaster())); requestMasterButton.Enabled = false;
        box.Controls.Add(actionsRow); box.Controls.Add(connectionStatus);
        BuildRequestList(box);
        UpdateIconLabels();
    }
    void BuildGrid()
    {
        groupFont = new Font(Font, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(236, 239, 243);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(70, 79, 90);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 236, 249);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.DefaultCellStyle.Padding = new Padding(4, 5, 4, 5);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Color.FromArgb(235, 238, 242);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "included", HeaderText = Localization.WindowSyncCaption, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "title", HeaderText = Localization.T("窗口"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180, DefaultCellStyle = new() { WrapMode = DataGridViewTriState.True } });
        grid.Columns.Add(new DataGridViewColumn(new OffsetCell()) { Name = "offset", HeaderText = Localization.T("偏移(秒)"), Width = 180, MinimumWidth = 160, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "mute", HeaderText = Localization.T("静音"), AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = Localization.T("状态"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValidating += (_, e) => {
            if (refreshing || e.ColumnIndex != 2 || grid.Rows[e.RowIndex].Tag is not WindowRow || grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly) return;
            if (!TryOffset(e.FormattedValue?.ToString(), out int parsed)) { e.Cancel = true; grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = Localization.T("请输入 -86400 到 86400 的秒数"); }
            else grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
        };
        grid.DataError += (_, e) => { e.ThrowException = false; message = Localization.T("请检查输入的秒数"); };
        grid.CellValueChanged += (_, e) => ChangeRow(e.RowIndex, e.ColumnIndex);
        grid.ColumnWidthChanged += (_, _) => WrapTitles();
        grid.FontChanged += (_, _) => WrapTitles();
        grid.CellDoubleClick += (_, e) => {
            if (e.RowIndex < 0) return;
            string peer = grid.Rows[e.RowIndex].Tag switch { WindowRow row => row.Peer, GroupRow row => row.Peer, _ => "" };
            if (peer.Length == 0) return;
            ShowConnections(true);
        };
        grid.CellPainting += (_, e) => {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0) {
                e.PaintBackground(e.CellBounds, true); e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                var bounds = Rectangle.Inflate(e.CellBounds, -8, 0);
                TextRenderer.DrawText(e.Graphics!, grid.Columns[e.ColumnIndex].HeaderText, grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font, bounds, grid.ColumnHeadersDefaultCellStyle.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                e.Handled = true; return;
            }
            if (e.RowIndex >= 0 && (grid.Rows[e.RowIndex].Tag is GroupRow && e.ColumnIndex is 0 or 2 or 3 || grid.Rows[e.RowIndex].Tag is WindowRow { Primary: true } && e.ColumnIndex == 0)) {
                e.PaintBackground(e.CellBounds, true); e.Handled = true;
            }
        };
    }
    static string RttCaption(double? rtt) => Localization.T("往返") + " " + (rtt.HasValue ? $"{rtt:0} ms" : "—");
    void UpdatePlaybackIcon()
    {
        int state = requestedPlaybackState >= 0 ? requestedPlaybackState : playbackState;
        playbackButton.Symbol = state == 2 ? "pause" : "play";
        playbackButton.AccessibleName = Localization.T(state == 2 ? "暂停" : "开始播放");
        tooltips.SetToolTip(playbackButton, playbackButton.AccessibleName); playbackButton.Invalidate();
    }
    static string FindDocumentation() => new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")) }.Select(p => Path.Combine(p, Localization.Manual)).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(Localization.T("未找到使用说明"));
    void ChangeLanguage(string language, bool save = true)
    {
        bool editCommitted = !grid.IsCurrentCellInEditMode || grid.EndEdit();
        Localization.Select(language, save); Localization.Apply(this); grid.Columns["included"]!.HeaderText = Localization.WindowSyncCaption; UpdateIconLabels(); FitHeaders();
        AlignControlRows(); deviceFingerprint = ""; if (editCommitted) RebuildRows(); UpdateUi();
    }
    ToolStripDropDownMenu? settingsMenu;
    void ShowSettings() => ShowSettingsMenu(null);
    void ShowSettingsMenu(string? screenshot)
    {
        if (settingsMenu?.Visible == true) { settingsMenu.Close(); return; }
        settingsMenu?.Dispose();
        var menu = new ToolStripDropDownMenu { Font = Font, ShowImageMargin = false, ShowCheckMargin = true };
        settingsMenu = menu;
        string[] captions = ["English", "简体中文", "繁體中文"], codes = ["en", "zh-Hans", "zh-Hant"];
        for (int i = 0; i < codes.Length; i++) {
            string code = codes[i];
            var item = new ToolStripMenuItem(captions[i]) { Checked = code == Localization.Language, Padding = new Padding(8, 5, 14, 5) };
            item.Click += (_, _) => { menu.Close(); try { ChangeLanguage(code); } catch (Exception ex) { MessageBox.Show(ex.Message, "SyncPlayer"); } };
            menu.Items.Add(item);
        }
        IconButton? FindGear(Control root) {
            foreach (Control child in root.Controls) {
                if (child is IconButton { Symbol: "settings" } gear) return gear;
                if (FindGear(child) is IconButton found) return found;
            }
            return null;
        }
        var gear = FindGear(this)!; menu.Show(gear, new Point(gear.Width - menu.PreferredSize.Width, gear.Height));
        if (screenshot != null) {
            using var bitmap = new Bitmap(menu.Width, menu.Height); menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size)); bitmap.Save(screenshot); menu.Close();
        }
    }
    void ShowConnections(bool visible)
    {
        if (remoteConnectionSwitch.Checked != visible) SetSyncMode(visible ? SyncMode.Remote : SyncMode.Local);
        else SetConnectionPanelVisible(visible);
    }
    void SetConnectionPanelVisible(bool visible)
    {
        if (connectionPanel.Visible == visible) return;
        if (visible) {
            connectionPanel.Visible = true; PerformLayout();
            int before = Height; Height = Math.Min(Screen.FromControl(this).WorkingArea.Height - 30, Height + connectionPanel.PreferredSize.Height);
            connectionExtraHeight = Height - before;
        } else {
            connectionPanel.Visible = false; Height = Math.Max(MinimumSize.Height, Height - connectionExtraHeight); connectionExtraHeight = 0;
        }
    }
    static void OpenManual()
    {
        string manual = FindDocumentation();
        try { Process.Start(new ProcessStartInfo(manual) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) {
            var viewer = new ProcessStartInfo("notepad.exe");
            viewer.ArgumentList.Add(manual);
            Process.Start(viewer);
        }
    }
}
