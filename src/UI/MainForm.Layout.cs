using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm
{
    static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 7, 12, 7) };
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
    }
    void FitHeaders()
    {
        foreach (DataGridViewColumn column in grid.Columns)
            column.MinimumWidth = Math.Max(column.Name == "title" ? 180 : 0, TextRenderer.MeasureText(column.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Width + 32);
    }
    void AlignControlRows()
    {
        int height = Math.Max(master.PreferredHeight, TextRenderer.MeasureText("Ag", Font).Height + 10);
        foreach (var combo in new[] { master, localAddress, direction }) combo.ItemHeight = height - 8;
        height = master.PreferredHeight;
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
                } else if (child is ToggleSwitch toggle) {
                    toggle.Height = height;
                    toggle.Width = TextRenderer.MeasureText(toggle.Text, toggle.Font).Width + 2 * (height - 6) + 28;
                    toggle.Margin = new Padding(0, 0, 24, 8);
                } else if (child is TimeInput) {
                    child.Height = height; child.Margin = new Padding(0, 0, 8, 0);
                } else if (child is Label label && label != status) {
                    label.Anchor = AnchorStyles.Left;
                    label.Margin = new Padding(0, root is TableLayoutPanel ? 0 : Math.Max(0, (height - label.PreferredHeight) / 2), 12, 0);
                }
                if (child is not TimeInput) Walk(child);
            }
        }
        Walk(this); PerformLayout();
        direction.Width = Math.Max(160, direction.Items.Cast<object>().Select(i => TextRenderer.MeasureText(i.ToString(), direction.Font).Width + 48).DefaultIfEmpty(160).Max());
        direction.DropDownWidth = direction.Width;
    }
    Button Button(string text, Action action, bool discardEdit = false)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 3, 8, 3) };
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
        source.Controls.Add(MakeIcon("refresh", "刷新", () => { RefreshPlayers(); lan.RefreshAddresses(); lan.Search(); }), 2, 0);
        source.Controls.Add(MakeIcon("help", "? 说明", OpenManual, true), 3, 0);
        source.Controls.Add(MakeIcon("settings", "设置", ShowSettings, true), 4, 0);
        root.Controls.Add(source, 0, 0);
        var switches = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        switches.Controls.Add(localSyncSwitch); switches.Controls.Add(remoteConnectionSwitch); root.Controls.Add(switches, 0, 1);
        BuildConnectionPanel(); root.Controls.Add(connectionPanel, 0, 2);
        BuildGrid(); root.Controls.Add(grid, 0, 3);
        var playback = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
        playbackButton.Click += (_, _) => Command((requestedPlaybackState >= 0 ? requestedPlaybackState : playbackState) == 2 ? 1 : 2); playback.Controls.Add(playbackButton);
        playback.Controls.Add(Button(Localization.T("同步"), Align));
        playback.Controls.Add(Button(Localization.T("取消偏移"), ResetOffsets, discardEdit: true));
        playback.Controls.Add(details); root.Controls.Add(playback, 0, 4);
        var jump = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        jump.Controls.Add(seek); jump.Controls.Add(Label(Localization.T("秒"))); jump.Controls.Add(Button(Localization.T("跳转"), Jump)); root.Controls.Add(jump, 0, 5);
        seek.CommitRequested += (_, _) => { try { seek.Value = seek.Value; playbackButton.Focus(); } catch (FormatException) { message = Localization.T("请检查输入的秒数"); } };
        root.Controls.Add(status, 0, 6);
        details.ForeColor = Color.FromArgb(90, 98, 110); status.ForeColor = details.ForeColor;
        root.SizeChanged += (_, _) => { details.MaximumSize = new Size(Math.Max(160, root.ClientSize.Width - 40), 0); status.MaximumSize = details.MaximumSize; };
        master.SelectedIndexChanged += (_, _) => { if (!refreshing) { RebuildRows(); UpdateBinding(); } };
        master.DropDown += (_, _) => master.DropDownWidth = Math.Min(Screen.FromControl(this).WorkingArea.Width - 40, Math.Max(master.Width, localPlayers.Select(p => TextRenderer.MeasureText(p.ToString(), master.Font).Width + 32).DefaultIfEmpty().Max()));
        localSyncSwitch.CheckedChanged += (_, _) => { if (!grid.EndEdit()) grid.CancelEdit(); RebuildRows(); UpdateBinding(); };
        remoteConnectionSwitch.CheckedChanged += (_, _) => {
            if (!grid.EndEdit()) grid.CancelEdit();
            localSyncSwitch.Checked = !remoteConnectionSwitch.Checked;
            lan.SetEnabled(remoteConnectionSwitch.Checked);
            if (remoteConnectionSwitch.Checked) lan.Start();
            SetConnectionPanelVisible(remoteConnectionSwitch.Checked); RebuildRows();
        };
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
        addressRow.Controls.Add(Label(Localization.T("本机地址"))); addressRow.Controls.Add(localAddress);
        addressRow.Controls.Add(Button(Localization.T("复制地址"), () => { if (localAddress.SelectedItem is string address) Clipboard.SetText(address); else message = Localization.T("当前没有局域网地址"); }));
        box.Controls.Add(addressRow);
        var destinations = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        devices.ItemHeight = Font.Height + 10;
        devices.DrawItem += (_, e) => {
            if (e.Index < 0 || devices.Items[e.Index] is not DeviceChoice entry) return;
            e.DrawBackground();
            string name = DeviceIdentity.DisplayName(entry.Device, lan.Devices, lan.Name);
            string latency = entry.Device.Connected ? entry.Device.Rtt.HasValue ? $"{entry.Device.Rtt:0.#} ms" : "—" : "";
            int latencyWidth = TextRenderer.MeasureText(latency, e.Font).Width + 14;
            using var dot = new SolidBrush(entry.Device.Connected ? Color.FromArgb(65, 72, 82) : Color.FromArgb(190, 195, 203));
            e.Graphics.FillEllipse(dot, e.Bounds.Left + 7, e.Bounds.Top + e.Bounds.Height / 2 - 3, 6, 6);
            var left = new Rectangle(e.Bounds.Left + 23, e.Bounds.Top, Math.Max(1, e.Bounds.Width - latencyWidth - 23), e.Bounds.Height);
            var right = new Rectangle(e.Bounds.Right - latencyWidth, e.Bounds.Top, latencyWidth - 7, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, name, e.Font, left, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, latency, e.Font, right, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Right);
            e.DrawFocusRectangle();
        };
        destinations.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); destinations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        destinations.Controls.Add(Label(Localization.T("设备")), 0, 0); destinations.Controls.Add(devices, 1, 0); destinations.Controls.Add(Label(Localization.T("对方地址")), 0, 1); destinations.Controls.Add(manualAddress, 1, 1);
        box.Controls.Add(destinations);
        direction.Items.AddRange(new object[] { Localization.T("我控制对方"), Localization.T("对方控制我"), Localization.T("双向") }); direction.SelectedIndex = 0;
        var actionsRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        actionsRow.Controls.Add(direction); actionsRow.Controls.Add(Button(Localization.T("连接"), Connect));
        actionsRow.Controls.Add(Button(Localization.T("断开"), () => { if (devices.SelectedItem is DeviceChoice selected) lan.Disconnect(selected.Device.Id); }));
        box.Controls.Add(actionsRow); box.Controls.Add(connectionStatus);
        incomingBar.Controls.Add(incomingText);
        incomingBar.Controls.Add(Button(Localization.T("接受"), () => { lan.Respond(incomingNonce, true); incomingNonce = ""; }));
        incomingBar.Controls.Add(Button(Localization.T("拒绝"), () => { lan.Respond(incomingNonce, false); incomingNonce = ""; })); box.Controls.Add(incomingBar);
        devices.SelectedIndexChanged += (_, _) => { if (!refreshing && devices.SelectedIndex >= 0) manualAddress.Clear(); };
        manualAddress.TextChanged += (_, _) => { if (!refreshing && manualAddress.TextLength > 0) devices.SelectedIndex = -1; };
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
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "included", HeaderText = Localization.T("跟随"), AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
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
            ShowConnections(true); devices.SelectedIndex = devices.Items.Cast<DeviceChoice>().ToList().FindIndex(d => d.Device.Id == peer);
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
    string DeviceCaption(DeviceView device, DeviceView[] all)
    {
        string name = DeviceIdentity.DisplayName(device, all, lan.Name);
        string state = Localization.T(device.Status);
        return name + " · " + state + (device.Connected && device.Rtt.HasValue ? $" · {device.Rtt:0.#} ms" : "");
    }
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
        Localization.Select(language, save); Localization.Apply(this); UpdateIconLabels(); FitHeaders();
        int selected = direction.SelectedIndex;
        direction.Items.Clear(); direction.Items.AddRange(new object[] { Localization.T("我控制对方"), Localization.T("对方控制我"), Localization.T("双向") }); direction.SelectedIndex = selected;
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
        if (remoteConnectionSwitch.Checked != visible) remoteConnectionSwitch.Checked = visible;
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
