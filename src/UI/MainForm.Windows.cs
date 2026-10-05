using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm
{
    static bool TryOffset(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!(decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) || number < -86400 || number > 86400) return false;
        value = (int)Math.Round(number * 1000); return true;
    }
    void SaveDeviceName()
    {
        if (deviceName.Text == lan.Name) return;
        try { string name = DeviceIdentity.Validate(deviceName.Text); DeviceIdentity.SaveName(name); lan.Rename(name); deviceName.Text = name; deviceFingerprint = ""; message = Localization.T("名称已保存"); }
        catch (Exception ex) { message = ex.Message; deviceName.Text = lan.Name; }
    }
    void Connect()
    {
        string destination = manualAddress.Text.Trim();
        if (destination.Length == 0 && devices.SelectedItem is DeviceChoice selected) destination = selected.Device.Address;
        if (destination.Length == 0) { message = Localization.T("请选择设备或粘贴对方地址"); return; }
        try { lan.Connect(destination, (ControlDirection)direction.SelectedIndex); message = Localization.T("等待对方确认"); }
        catch (ArgumentException) { message = Localization.T("地址无法识别，请复制对方显示的地址"); }
        catch (Exception ex) { message = ex.Message; }
    }
    void RefreshPlayers()
    {
        var players = PotPlayer.List();
        var signature = string.Join('|', players.Select(p => $"{p.Handle}:{p.Title}"));
        if (signature == windowFingerprint && master.Items.Count > 0) return;
        windowFingerprint = signature; refreshing = true;
        nint old = (master.SelectedItem as PlayerWindow)?.Handle ?? 0;
        localPlayers = players;
        master.Items.Clear(); master.Items.AddRange(players.Cast<object>().ToArray());
        if (players.Count > 0) master.SelectedIndex = Math.Max(0, players.FindIndex(p => p.Handle == old));
        foreach (var player in players) if (!localOptions.ContainsKey(player.Handle)) localOptions[player.Handle] = new();
        foreach (var handle in localOptions.Keys.Where(h => !players.Any(p => p.Handle == h)).ToArray()) localOptions.TryRemove(handle, out _);
        lan.Publish(players.Select(p => new LanWindow(p.Handle.ToString("X"), p.ToString())));
        refreshing = false; RebuildRows(); UpdateBinding();
    }
    void RebuildRows()
    {
        if (grid.IsCurrentCellInEditMode && !grid.EndEdit()) return;
        refreshing = true;
        string? current = grid.CurrentRow?.Tag is WindowRow selected ? $"{selected.Peer}:{selected.Window}" : null;
        grid.Rows.Clear(); AddGroup(Localization.T("本机"), "", "");
        nint primary = (master.SelectedItem as PlayerWindow)?.Handle ?? 0;
        foreach (var player in localPlayers) {
            if (!localSyncSwitch.Checked && player.Handle != primary) continue;
            var options = localOptions[player.Handle]; bool main = player.Handle == primary;
            var row = grid.Rows[grid.Rows.Add(!main && options.Included, player.ToString(), FormatOffset(options.Offset), actualMute.GetValueOrDefault(player.Handle), main ? Localization.T("主窗口") : options.Included ? Localization.T("跟随") : "")];
            row.Tag = new WindowRow(player.Handle, "", player.Handle.ToString("X"), main);
            row.Cells[0].ReadOnly = main || !localSyncSwitch.Checked;
            row.Cells[1].ToolTipText = player.ToString();
        }
        var peerList = lan.Devices;
        foreach (var device in peerList.Where(_ => remoteConnectionSwitch.Checked)) {
            AddGroup(DeviceIdentity.DisplayName(device, peerList, lan.Name), device.Id, device.Connected ? device.Rtt.HasValue ? $"{device.Rtt:0.#} ms" : Localization.T("已连接") : device.Status);
            foreach (var window in device.Windows) {
                string key = device.Id + ":" + window.Id;
                if (!remoteOptions.TryGetValue(key, out var options)) remoteOptions[key] = options = new();
                bool editable = device.Connected && device.CanSend;
                var row = grid.Rows[grid.Rows.Add(editable && options.Included, window.Name, FormatOffset(options.Offset), window.Muted ?? options.Muted ?? false, device.Connected ? device.CanSend ? Localization.T("可控制") : Localization.T("接收控制") : Localization.T("未连接"))];
                row.Tag = new WindowRow(0, device.Id, window.Id);
                row.Cells[0].ReadOnly = !editable; row.Cells[2].ReadOnly = row.Cells[3].ReadOnly = !editable;
                row.Cells[1].ToolTipText = window.Name;
                if (!editable) row.DefaultCellStyle.ForeColor = Color.Gray;
            }
            if (device.Windows.Length == 0) {
                var row = grid.Rows[grid.Rows.Add(false, Localization.T("未打开视频"), "", false, "")]; row.ReadOnly = true; row.Tag = new GroupRow(device.Id);
            }
        }
        foreach (DataGridViewRow row in grid.Rows) if (row.Tag is WindowRow window && $"{window.Peer}:{window.Window}" == current) { grid.CurrentCell = row.Cells[1]; break; }
        refreshing = false;
        WrapTitles();
    }
    static string FormatOffset(int milliseconds) => (milliseconds / 1000m).ToString("0.0##", CultureInfo.CurrentCulture);
    void WrapTitles()
    {
        if (!grid.Columns.Contains("title")) return;
        int width = Math.Max(40, grid.Columns["title"]!.Width - 20);
        foreach (DataGridViewRow row in grid.Rows) {
            if (row.Tag is not WindowRow || row.Cells[1].ToolTipText.Length == 0) continue;
            string line = ""; var result = new System.Text.StringBuilder();
            foreach (var rune in row.Cells[1].ToolTipText.EnumerateRunes()) {
                string next = rune.ToString();
                if (line.Length > 0 && TextRenderer.MeasureText(line + next, grid.Font, Size.Empty, TextFormatFlags.NoPadding).Width > width) { result.AppendLine(line); line = ""; }
                line += next;
            }
            result.Append(line); string wrapped = result.ToString();
            if (!Equals(row.Cells[1].Value, wrapped)) row.Cells[1].Value = wrapped;
        }
    }
    void AddGroup(string title, string peer, string state)
    {
        var row = grid.Rows[grid.Rows.Add(false, title, "", false, Localization.T(state))]; row.Tag = new GroupRow(peer); row.ReadOnly = true;
        row.DefaultCellStyle.BackColor = Color.FromArgb(242, 245, 248); row.DefaultCellStyle.Font = groupFont;
    }
    void ChangeRow(int rowIndex, int column)
    {
        if (refreshing || rowIndex < 0 || column is not (0 or 2 or 3) || grid.Rows[rowIndex].Tag is not WindowRow row) return;
        var cells = grid.Rows[rowIndex].Cells;
        var options = row.Peer.Length == 0 ? localOptions[row.Handle] : remoteOptions[row.Peer + ":" + row.Window];
        if (column == 0) { options.Included = cells[0].Value is true; UpdateBinding(); }
        if (column == 2 && TryOffset(cells[2].Value?.ToString(), out int offset)) {
            options.Offset = offset;
            if (string.IsNullOrWhiteSpace(cells[2].Value?.ToString())) { refreshing = true; cells[2].Value = FormatOffset(0); refreshing = false; }
            UpdateBinding(false);
        }
        if (column == 3) options.Muted = cells[3].Value is true;
        bool included = options.Included; int bias = options.Offset; bool? muted = options.Muted;
        if (row.Peer.Length == 0) {
            RunControl(() => {
                if (row.Primary && column == 2 && masterHandle == row.Handle) {
                    var source = PotPlayer.Read(masterHandle);
                    int position = PlaybackTimeline.Position((double)source.Position + bias - appliedMainOffset, 0, source.Duration);
                    AlignLocal(position: position); appliedMainOffset = bias;
                    detector.Reset(false); detector.Observe(PotPlayer.Read(masterHandle)); Broadcast(Event(PotPlayer.Read(masterHandle)));
                    return;
                }
                if (column == 3) { PotPlayer.Mute(row.Handle, muted == true); actualMute[row.Handle] = muted == true; }
                if ((column == 2 || column == 0 && included) && included && masterHandle != 0 && row.Handle != masterHandle) {
                    PotPlayer.Prepare(row.Handle); AlignLocal(); detector.Reset(false); detector.Observe(PotPlayer.Read(masterHandle));
                }
            });
        } else if (column is 0 or 2 or 3) {
            RunControl(() => {
                WireEvent? operation = null;
                if (included && column != 3 && masterHandle != 0) { var sample = PotPlayer.Read(masterHandle); operation = Event(sample); }
                lan.SendControl(row.Peer, operation, [new(row.Window, bias - mainOffset, column == 3 ? muted : null)]);
            });
        }
    }
    void UpdateBinding(bool prepare = true)
    {
        if (refreshing) return;
        nint primary = (master.SelectedItem as PlayerWindow)?.Handle ?? 0;
        var nextTargets = localPlayers.Where(p => localSyncSwitch.Checked && p.Handle != primary && localOptions[p.Handle].Included).Select(p => p.Handle).ToArray();
        int nextMainOffset = localOptions.TryGetValue(primary, out var primaryOptions) ? primaryOptions.Offset : 0;
        var nextOffsets = nextTargets.ToDictionary(h => h, h => localOptions[h].Offset - nextMainOffset);
        bool linked = localSyncSwitch.Checked;
        actions.Enqueue(() => { bool changed = masterHandle != primary || !localFollowers.SequenceEqual(nextTargets) || syncing != linked; if (masterHandle != primary) appliedMainOffset = nextMainOffset; masterHandle = primary; mainOffset = nextMainOffset; localFollowers = nextTargets; localOffsets = nextOffsets; syncing = linked; if (changed) detector.Reset(false); needsPrepare |= prepare && changed; });
    }
    void ResetOffsets()
    {
        grid.CancelEdit();
        foreach (var option in localOptions.Values) option.Offset = 0;
        foreach (var option in remoteOptions.Values) option.Offset = 0;
        var remoteTargets = remoteOptions.Keys.Select(key => {
            int separator = key.IndexOf(':');
            return (Peer: key[..separator], Window: key[(separator + 1)..]);
        }).GroupBy(target => target.Peer).ToArray();
        RebuildRows(); UpdateBinding(false);
        RunControl(() => {
            // Reset the baseline without seeking the source back by its old bias.
            appliedMainOffset = 0;
            foreach (var device in remoteTargets)
                lan.SendControl(device.Key, null, device.Select(target => new RemoteTarget(target.Window, 0)).ToArray());
            if (masterHandle != 0) {
                AlignLocal();
                var source = PotPlayer.Read(masterHandle);
                detector.Reset(false); detector.Observe(source); Broadcast(Event(source));
            }
            message = Localization.T("已取消偏移");
        });
    }
    void UpdateUi()
    {
        if (IsDisposed) return;
        if ((DateTime.Now - lastWindowRefresh).TotalSeconds >= 2 && !grid.IsCurrentCellInEditMode) { lastWindowRefresh = DateTime.Now; RefreshPlayers(); }
        var addressList = lan.Addresses;
        if (!localAddress.Items.Cast<string>().SequenceEqual(addressList)) {
            var old = localAddress.SelectedItem as string; localAddress.Items.Clear(); localAddress.Items.AddRange(addressList); localAddress.Enabled = addressList.Length > 0;
            if (addressList.Length > 0) localAddress.SelectedIndex = Math.Max(0, Array.IndexOf(addressList, old));
        }
        var snapshot = lan.Devices;
        string signature = string.Join('|', snapshot.Select(d => $"{d.Id}:{d.Name}:{d.Address}:{d.Connected}:{d.CanSend}:{d.Status}:{string.Join(',', d.Windows.Select(w => w.Id + w.Name))}"));
        if (signature != deviceFingerprint && !grid.IsCurrentCellInEditMode) {
            deviceFingerprint = signature; string? selectedId = (devices.SelectedItem as DeviceChoice)?.Device.Id;
            refreshing = true; devices.Items.Clear(); devices.Items.AddRange(snapshot.Select(d => (object)new DeviceChoice(d, DeviceCaption(d, snapshot))).ToArray());
            devices.SelectedIndex = Array.FindIndex(snapshot, d => d.Id == selectedId); refreshing = false;
            RebuildRows();
        }
        for (int i = 0; i < devices.Items.Count; i++) if (devices.Items[i] is DeviceChoice entry && snapshot.FirstOrDefault(d => d.Id == entry.Device.Id) is DeviceView live) {
            string caption = DeviceCaption(live, snapshot);
            if (caption == entry.Caption) continue;
            refreshing = true; devices.Items[i] = new DeviceChoice(live, caption); refreshing = false;
        }
        foreach (DataGridViewRow row in grid.Rows) {
            if (row.Tag is WindowRow { Peer.Length: 0, Primary: false } follower && localOptions.TryGetValue(follower.Handle, out var followerOptions)) {
                int side = localBoundary.GetValueOrDefault(follower.Handle);
                row.Cells[4].Value = !followerOptions.Included ? "" : !localSyncSwitch.Checked ? Localization.T("未启用") : side < 0 ? Localization.T("片头等待") : side > 0 ? Localization.T("片尾停住") : Localization.T("跟随");
            }
            if (row.Tag is WindowRow local && local.Peer.Length == 0 && grid.CurrentCell != row.Cells[3]) { refreshing = true; row.Cells[3].Value = actualMute.GetValueOrDefault(local.Handle); refreshing = false; }
            if (row.Tag is WindowRow remote && remote.Peer.Length > 0 && grid.CurrentCell != row.Cells[3]) {
                var window = snapshot.FirstOrDefault(d => d.Id == remote.Peer)?.Windows.FirstOrDefault(w => w.Id == remote.Window);
                if (window?.Muted is bool mute) { refreshing = true; row.Cells[3].Value = mute; refreshing = false; }
            }
            if (row.Tag is GroupRow group && group.Peer.Length > 0) { var device = snapshot.FirstOrDefault(d => d.Id == group.Peer); if (device != null) row.Cells[4].Value = device.Connected ? device.Rtt.HasValue ? $"{device.Rtt:0.#} ms" : Localization.T("已连接") : Localization.T(device.Status); }
        }
        connectionStatus.Text = Localization.T(devices.SelectedItem is DeviceChoice choice ? snapshot.FirstOrDefault(d => d.Id == choice.Device.Id)?.Status ?? "" : lan.DiscoveryStatus);
        int connections = snapshot.Count(d => d.Connected);
        if (connections > 0) connectionStatus.Text += " · " + Localization.F("{0} 台已连接", connections);
        var request = lan.Requests.FirstOrDefault();
        incomingBar.Visible = request != null;
        if (request != null) {
            incomingNonce = request.Nonce; ShowConnections(true);
            incomingText.Text = request.Name + Localization.T(" 请求：") + (request.Direction == ControlDirection.Send ? Localization.T("控制本机") : request.Direction == ControlDirection.Receive ? Localization.T("由本机控制") : Localization.T("双向控制"));
        }
        status.Text = Localization.T(message); details.Text = readings;
        if (requestedPlaybackState < 0 && masterHandle != 0) { try { playbackState = PotPlayer.Read(masterHandle, 20486); } catch (IOException) { playbackState = 0; } }
        UpdatePlaybackIcon();
    }
}
