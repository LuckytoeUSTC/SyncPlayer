using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm
{
    void RunControl(Action action)
    {
        if (IsHandleCreated && !InvokeRequired) { actions.Enqueue(action); return; }
        lock (gate) { DrainActions(); TryAction(action); }
    }
    void TryAction(Action action) { try { action(); } catch (Exception ex) { message = Localization.T("操作未完成，请检查窗口或连接"); Trace(ex.ToString()); } }
    void DrainActions() { while (actions.TryDequeue(out var action)) TryAction(action); }
    void AlignLocal(int? state = null, int? position = null)
    {
        if (masterHandle == 0) throw new IOException(Localization.T("请选择主窗口"));
        Trace($"align source={masterHandle:X} state={state} position={position} localFollowers={string.Join(',', localFollowers.Select(h => $"{h:X}:{localOffsets.GetValueOrDefault(h)}"))}");
        foreach (var h in new[] { masterHandle }.Concat(localFollowers)) PotPlayer.Prepare(h);
        PotPlayer.Align(masterHandle, localFollowers, state, position, localOffsets);
        var source = PotPlayer.Read(masterHandle);
        foreach (var h in localFollowers) localBoundary[h] = PlaybackTimeline.Boundary(source.Position, localOffsets.GetValueOrDefault(h), PotPlayer.Read(h, 20482));
        needsPrepare = false;
    }
    bool UpdateLocalBoundaries(PlaybackSample source)
    {
        if (!syncing || source.State != 2) return false;
        bool rejoin = false;
        foreach (var h in localFollowers) {
            int duration = PotPlayer.Read(h, 20482), offset = localOffsets.GetValueOrDefault(h);
            int side = PlaybackTimeline.Boundary(source.Position, offset, duration);
            int previous = localBoundary.GetValueOrDefault(h);
            if (side == previous) continue;
            localBoundary[h] = side;
            if (side == 0) rejoin = true;
            else PotPlayer.Hold(h, PlaybackTimeline.Position(source.Position, offset, duration));
        }
        if (!rejoin) return false;
        AlignLocal(); detector.Reset(false); detector.Observe(PotPlayer.Read(masterHandle));
        return true;
    }
    void UpdateRemoteBoundaries()
    {
        double at = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        foreach (var pair in remoteClocks.ToArray()) {
            var clock = pair.Value;
            if (!lan.Devices.Any(p => p.Id == clock.Peer && p.Connected && p.CanReceive)) { remoteClocks.Remove(pair.Key); continue; }
            if (clock.State != 2) continue;
            int side = PlaybackTimeline.Boundary(clock.Now(at), clock.Offset, clock.Duration);
            if (side == clock.Boundary) continue;
            clock.Boundary = side;
            try {
                PotPlayer.Hold(pair.Key, PlaybackTimeline.Position(clock.Now(at), clock.Offset, clock.Duration));
                PotPlayer.Speed(pair.Key, clock.Speed);
                if (side == 0) PotPlayer.State(pair.Key, 2);
            } catch (IOException) { remoteClocks.Remove(pair.Key); }
        }
    }
    void Command(int state)
    {
        long version = Interlocked.Increment(ref playbackVersion);
        requestedPlaybackState = state; UpdatePlaybackIcon();
        RunControl(() => {
        try {
        if (version != Interlocked.Read(ref playbackVersion)) return;
        if (masterHandle == 0) { message = Localization.T("请选择主窗口"); return; }
        if (state == 2 && localFollowers.Length > 0) AlignLocal(2);
        else foreach (var h in new[] { masterHandle }.Concat(localFollowers)) PotPlayer.State(h, state);
        var sample = PotPlayer.Read(masterHandle); detector.Reset(false); detector.Observe(sample);
        Broadcast(state == 2 ? Event(sample) : new WireEvent { type = "event", state = 1 });
        message = state == 2 ? Localization.T("播放") : Localization.T("暂停");
        } finally { if (version == Interlocked.Read(ref playbackVersion)) requestedPlaybackState = -1; }
        });
    }
    void Jump()
    {
        int position = (int)(seek.Value * 1000); long version = Interlocked.Increment(ref jumpVersion);
        RunControl(() => { if (version != Interlocked.Read(ref jumpVersion)) return; AlignLocal(position: position); var sample = PotPlayer.Read(masterHandle); detector.Reset(false); detector.Observe(sample); Broadcast(Event(sample)); message = Localization.T("已跳转"); });
    }
    void Align() => RunControl(() => {
        long version = input.Version; AlignLocal(); var sample = PotPlayer.Read(masterHandle); detector.Reset(false); detector.Observe(sample);
        if (input.Version != version) detector.Reset(true);
        Broadcast(Event(sample)); message = Localization.T("已对齐");
    });
    static WireEvent Event(PlaybackSample sample) => new() { type = "event", cur = sample.Position, total = sample.Duration, state = sample.State, speed = sample.Speed };
    void Broadcast(WireEvent operation)
    {
        if (Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency < suppressNetworkUntil) return;
        foreach (var device in lan.Devices.Where(d => d.Connected && d.CanSend)) {
            var selected = device.Windows.Where(w => remoteOptions.TryGetValue(device.Id + ":" + w.Id, out var option) && option.Included)
                .Select(w => new RemoteTarget(w.Id, remoteOptions[device.Id + ":" + w.Id].Offset - mainOffset)).ToArray();
            lan.SendControl(device.Id, operation, selected);
        }
    }
    void ReceiveRemote(WireEvent? operation, RemoteTarget[] selections, string source)
    {
        actions.Enqueue(() => {
            if (!lan.Devices.Any(p => p.Id == source && p.Connected && p.CanReceive)) return;
            var current = PotPlayer.List();
            var selected = selections.Select(t => (Target: t, PlayerWindow: current.FirstOrDefault(p => p.Handle.ToString("X") == t.Id))).Where(p => p.PlayerWindow != null).ToArray();
            if (selected.Length == 0) return;
            suppressNetworkUntil = double.PositiveInfinity;
            try {
                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                foreach (var pair in selected) if (operation != null) {
                    nint handle = pair.PlayerWindow!.Handle;
                    if (!remoteClocks.TryGetValue(handle, out var clock)) clock = new() { Position = PotPlayer.Read(handle, 20484) - pair.Target.Offset, At = now, State = PotPlayer.Read(handle, 20486), Speed = PotPlayer.Read(handle, 20501) };
                    clock.Position = operation.cur ?? clock.Now(now); clock.At = now;
                    clock.Offset = pair.Target.Offset; clock.Duration = PotPlayer.Read(handle, 20482); clock.Peer = source;
                    if (operation.state.HasValue) clock.State = operation.state.Value;
                    if (operation.speed.HasValue) clock.Speed = operation.speed.Value;
                    clock.Boundary = PlaybackTimeline.Boundary(clock.Position, clock.Offset, clock.Duration);
                    remoteClocks[handle] = clock;
                }
                foreach (var pair in selected) {
                    var handle = pair.PlayerWindow!.Handle;
                    if (pair.Target.Muted.HasValue) { PotPlayer.Mute(handle, pair.Target.Muted.Value); actualMute[handle] = pair.Target.Muted.Value; }
                    if (operation?.speed is int speed) PotPlayer.Speed(handle, speed);
                }
                if (operation?.cur is int position) {
                    var primary = selected[0]; nint handle = primary.PlayerWindow!.Handle;
                    foreach (var pair in selected) PotPlayer.Prepare(pair.PlayerWindow!.Handle);
                    var bias = selected.Skip(1).ToDictionary(p => p.PlayerWindow!.Handle, p => p.Target.Offset);
                    PotPlayer.Align(handle, selected.Skip(1).Select(p => p.PlayerWindow!.Handle), remoteClocks[handle].State, position, bias, primary.Target.Offset, externalSource: true);
                    foreach (var pair in selected) {
                        var sample = PotPlayer.Read(pair.PlayerWindow!.Handle); var clock = remoteClocks[pair.PlayerWindow.Handle];
                        if (clock.State == 2 && sample.State == 2) {
                            double basis = sample.Position - clock.Offset;
                            foreach (var item in selected) { var other = remoteClocks[item.PlayerWindow!.Handle]; other.Position = basis; other.At = sample.At; }
                            break;
                        }
                    }
                } else if (operation?.state is int state) foreach (var pair in selected) {
                    var clock = remoteClocks[pair.PlayerWindow!.Handle];
                    PotPlayer.State(pair.PlayerWindow.Handle, PlaybackTimeline.State(clock.Now(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency), clock.Offset, clock.Duration, state));
                }
                if (selected.Any(p => p.PlayerWindow!.Handle == masterHandle)) { detector.Reset(false); detector.Observe(PotPlayer.Read(masterHandle)); }
                message = Localization.F("已接收 · {0}", lan.Devices.FirstOrDefault(p => p.Id == source)?.Name ?? source);
            } finally { suppressNetworkUntil = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency + .4; }
        });
    }
    async Task Loop()
    {
        while (!cancel.IsCancellationRequested) {
            lock (gate) {
                DrainActions();
                try {
                    if (masterHandle != 0) {
                        if (needsPrepare) {
                            if (syncing && localFollowers.Length > 0) AlignLocal();
                            needsPrepare = false; detector.Reset(false);
                        }
                        var sample = PotPlayer.Read(masterHandle); var change = detector.Observe(sample, input.NavigationRecent); bool speedChanged = detector.SpeedChanged;
                        if (change.seek) input.Consume();
                        if (change.seek || change.state || speedChanged) Trace($"position={sample.Position} state={sample.State} speed={sample.Speed} event={change}");
                        if (syncing) {
                            bool aligned = localFollowers.Length > 0 && (change.seek || change.state && sample.State == 2);
                            if (aligned) {
                                long version = input.Version; AlignLocal(); AutomaticSeeks += localFollowers.Length;
                                sample = PotPlayer.Read(masterHandle); detector.Reset(false); detector.Observe(sample);
                                if (input.Version != version) detector.Reset(true);
                            } else {
                                if (speedChanged) {
                                    Parallel.ForEach(localFollowers, h => PotPlayer.Speed(h, sample.Speed));
                                    if (sample.State == 2 && localFollowers.Length > 0) PotPlayer.SettleRate(masterHandle, localFollowers, localOffsets, sample.Speed);
                                }
                                if (change.state) foreach (var h in localFollowers) PotPlayer.State(h, sample.State);
                            }
                        }
                        if (UpdateLocalBoundaries(sample)) { sample = PotPlayer.Read(masterHandle); Broadcast(Event(sample)); }
                        if (change.seek || change.state || speedChanged) Broadcast(change.seek || change.state && sample.State == 2 ? Event(sample) : new WireEvent { type = "event", state = change.state ? sample.State : null, speed = speedChanged ? sample.Speed : null });
                        playbackState = sample.State; readings = $"{TimeSpan.FromMilliseconds(Math.Max(0, sample.Position)):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(Math.Max(0, sample.Duration)):hh\\:mm\\:ss}   {sample.Speed / 1000.0:0.##}×";
                        if (sample.At - lastMutePoll > .8) {
                            var players = localPlayers;
                            foreach (var h in players.Select(p => p.Handle)) { try { actualMute[h] = PotPlayer.Muted(h); } catch (IOException) { } }
                            lan.Publish(players.Select(p => new LanWindow(p.Handle.ToString("X"), p.ToString(), actualMute.GetValueOrDefault(p.Handle))));
                            lastMutePoll = sample.At;
                        }
                    } else { playbackState = 0; readings = ""; message = Localization.T("打开视频后选择主窗口"); }
                    UpdateRemoteBoundaries();
                } catch (Exception ex) { message = Localization.T("窗口未响应，请刷新或重新选择"); Trace(ex.ToString()); }
            }
            try { await Task.Delay(30, cancel.Token); } catch (OperationCanceledException) { break; }
        }
    }
    public void StopEngine()
    {
        cancel.Cancel(); lock (gate) { if (stopped) return; stopped = true; lan.Dispose(); input.Dispose(); PotPlayer.RestorePreferences(); }
    }
    void Trace(string text) { trace.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {text}"); while (trace.Count > 160) trace.Dequeue(); }
    public string[] Trace() { lock (gate) return trace.ToArray(); }
}
