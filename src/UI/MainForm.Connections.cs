namespace SyncPlayer;

sealed partial class MainForm
{
    void SetSyncMode(SyncMode mode) {
        if(changingMode || syncMode == mode)return;
        changingMode = true;
        try {
            if(!grid.EndEdit())grid.CancelEdit();
            lock(gate) {
                while(actions.TryDequeue(out _)) { }
                Interlocked.Increment(ref playbackVersion); Interlocked.Increment(ref jumpVersion); requestedPlaybackState = -1;
                remoteClock = null; remotePrimarySignature = ""; localFollowers = []; localOffsets.Clear(); localBoundary.Clear();
                syncMode = mode;
                localSyncSwitch.Checked = mode == SyncMode.Local;
                remoteConnectionSwitch.Checked = mode == SyncMode.Remote;
                lan.SetEnabled(mode == SyncMode.Remote); detector.Reset(false); message = Localization.T("就绪");
            }
            SetConnectionPanelVisible(mode == SyncMode.Remote);
            RebuildRows(); UpdateBinding(); UpdateUi();
        } finally { changingMode = false; }
    }
    void BuildRequestList(TableLayoutPanel box)
    {
        connectedDeviceRow = new TableLayoutPanel { AutoSize = false, Height = Font.Height + 18, Dock = DockStyle.Top, ColumnCount = 2, Visible = false };
        connectedDeviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectedDeviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connectedDevice.AutoSize = false; connectedDevice.AutoEllipsis = true; connectedDevice.Dock = DockStyle.Fill;
        connectedDevice.Height = Font.Height + 12; connectedDevice.TextAlign = ContentAlignment.MiddleLeft;
        connectedDeviceRow.Controls.Add(connectedDevice, 0, 0); connectedDeviceRow.Controls.Add(connectedRtt, 1, 0);
        box.Controls.Add(connectedDeviceRow);
        incomingBar.Controls.Add(Label("申请列表"));
        applicants.ItemHeight = Font.Height + 10; applicants.DrawMode = DrawMode.OwnerDrawFixed;
        applicants.DrawItem += (_, e) => {
            if (e.Index < 0) return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, applicants.Items[e.Index].ToString(), e.Font, Rectangle.Inflate(e.Bounds, -6, 0), e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        applicants.SelectedIndexChanged += (_, _) => UpdateRequestButtons();
        incomingBar.Controls.Add(applicants);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        buttons.Controls.Add(acceptButton = Button("接受", () => RespondSelected(true)));
        buttons.Controls.Add(rejectButton = Button("拒绝", () => RespondSelected(false)));
        incomingBar.Controls.Add(buttons); box.Controls.Add(incomingBar); UpdateRequestButtons();
    }
    static LanWindow? PrimaryWindow(DeviceView device) => device.Windows.FirstOrDefault(w => w.Primary);
    void SyncRemotePrimary() {
        var device = lan.Devices.FirstOrDefault();
        var window = device == null ? null : PrimaryWindow(device);
        string signature = device?.Id + ":" + window?.Id + ":" + window?.Ready;
        if(signature == remotePrimarySignature)return;
        remotePrimarySignature = signature;
        if(lan.HasControl && window is {Ready:true} && masterHandle != 0 && masterDuration > 0)ObserveSource(broadcast:true);
    }
    void UpdateRequestButtons() {
        bool selected = applicants.SelectedItem is RequestChoice;
        if (acceptButton != null) acceptButton.Enabled = selected;
        if (rejectButton != null) rejectButton.Enabled = selected;
    }
    void RespondSelected(bool accept) {
        if (applicants.SelectedItem is RequestChoice choice) lan.Respond(choice.Request.Nonce, accept);
        UpdateRequestList();
    }
    void UpdateRequestList() {
        var requests = lan.Requests;
        string signature = Localization.Language + ":" + string.Join('|', requests.Select(r => r.Nonce + ":" + r.Name + ":" + r.Transfer));
        if (signature != requestFingerprint) {
            string? selected = (applicants.SelectedItem as RequestChoice)?.Request.Nonce;
            requestFingerprint = signature; applicants.BeginUpdate(); applicants.Items.Clear();
            applicants.Items.AddRange(requests.Select(r => (object)new RequestChoice(r)).ToArray());
            applicants.SelectedIndex = Array.FindIndex(requests, r => r.Nonce == selected); applicants.EndUpdate();
        }
        applicants.Height = Math.Clamp(requests.Length, 1, 3) * applicants.ItemHeight + (int)Math.Ceiling(4 * DeviceDpi / 96.0);
        incomingBar.Visible = requests.Length > 0;
        if (requests.Length > 0) ShowConnections(true);
        UpdateRequestButtons();
    }
    void UpdateConnectedDevice(DeviceView[] snapshot) {
        var device = snapshot.FirstOrDefault(d => d.Connected);
        if (connectedDeviceRow != null) connectedDeviceRow.Visible = device != null;
        connectedDevice.Text = device == null ? "" : Localization.F(device.CanSend ? "本机主控 · 对方受控 · {0}" : "本机受控 · 对方主控 · {0}", DeviceIdentity.DisplayName(device, snapshot, lan.Name));
        connectedRtt.Text = device == null ? "" : RttCaption(device.Rtt);
        tooltips.SetToolTip(connectedDevice, connectedDevice.Text);
        bool enabled = !lan.IsReceiver;
        playbackButton.Enabled = enabled; seek.Enabled = enabled;
        foreach (var button in new[]{syncButton, resetOffsetsButton, jumpButton}) if(button != null)button.Enabled = enabled;
    }
    string RemoteVideoHint(DeviceView[] snapshot) {
        if (snapshot.Length == 0) return "";
        if (masterHandle == 0 || masterDuration <= 0) return Localization.T("请打开视频");
        if (snapshot.Any(d => d.Connected && !d.Windows.Any(w => w.Ready))) return Localization.T("请对方打开视频");
        return "";
    }
}
