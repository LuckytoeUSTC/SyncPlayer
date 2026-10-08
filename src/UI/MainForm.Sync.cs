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
        AlignLocal(); ObserveSource();
        return true;
    }
    void UpdateRemoteBoundaries()
    {
        var clock = remoteClock;
        if(clock == null)return;
        if(syncMode != SyncMode.Remote || clock.Handle != masterHandle || !lan.Devices.Any(d=>d.CanReceive && d.Id==clock.Peer)) { remoteClock=null; return; }
        if(clock.State != 2)return;
        double at = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        int side = PlaybackTimeline.Boundary(clock.Now(at), clock.Offset, clock.Duration);
        if (side == clock.Boundary) return;
        clock.Boundary = side;
        try {
            PotPlayer.Hold(clock.Handle, PlaybackTimeline.Position(clock.Now(at), clock.Offset, clock.Duration));
            PotPlayer.Speed(clock.Handle, clock.Speed);
            if (side == 0) PotPlayer.State(clock.Handle, 2);
        } catch (IOException) { remoteClock = null; }
    }
    void Command(int state)
    {
        if (lan.IsReceiver) return;
        long version = Interlocked.Increment(ref playbackVersion);
        requestedPlaybackState = state; UpdatePlaybackIcon();
        RunControl(() => {
        try {
        if (version != Interlocked.Read(ref playbackVersion)) return;
        if (masterHandle == 0) { message = Localization.T("请选择主窗口"); return; }
        if (state == 2 && localFollowers.Length > 0) AlignLocal(2);
        else foreach (var h in new[] { masterHandle }.Concat(localFollowers)) PotPlayer.State(h, state);
        var sample = ObserveSource();
        Broadcast(state == 2 ? Event(sample) : new WireEvent { type = "event", state = 1 });
        message = state == 2 ? Localization.T("播放") : Localization.T("暂停");
        } finally { if (version == Interlocked.Read(ref playbackVersion)) requestedPlaybackState = -1; }
        });
    }
    void Jump()
    {
        if (lan.IsReceiver) return;
        int position = (int)(seek.Value * 1000); long version = Interlocked.Increment(ref jumpVersion);
        RunControl(() => { if (version != Interlocked.Read(ref jumpVersion)) return; AlignLocal(position: position); ObserveSource(broadcast: true); message = Localization.T("已跳转"); });
    }
    void Align() => RunControl(() => {
        if (lan.IsReceiver) return;
        long version = input.Version; AlignLocal(); ObserveSource(broadcast: true);
        if (input.Version != version) detector.Reset(true);
        message = Localization.T("已同步");
    });
    static WireEvent Event(PlaybackSample sample) => new() { type = "event", cur = sample.Position, total = sample.Duration, state = sample.State, speed = sample.Speed };
    PlaybackSample ObserveSource(bool broadcast = false) {
        var sample = PotPlayer.Read(masterHandle);
        detector.Reset(false);
        detector.Observe(sample);
        if(broadcast)Broadcast(Event(sample)); return sample;
    }
    void Broadcast(WireEvent operation)
    {
        if(syncMode != SyncMode.Remote || !lan.HasControl)return;
        var device = lan.Devices.FirstOrDefault();
        var window = device == null ? null : PrimaryWindow(device);
        if(device == null || window is not {Ready:true})return;
        var option = RemoteOptions(device.Id);
        lan.SendControl(device.Id, operation, new(window.Id, option.Offset-mainOffset));
    }
    void ReceiveRemote(WireEvent? operation, RemoteTarget target, string source)
    {
        long generation = lan.Generation;
        actions.Enqueue(() => {
            if(syncMode != SyncMode.Remote || generation != lan.Generation || !lan.IsReceiver || masterHandle == 0 || masterHandle.ToString("X") != target.Id)return;
            var peer = lan.Devices.FirstOrDefault(d => d.Id == source && d.CanReceive);
            if(peer == null || !WindowReady(masterHandle))return;
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            var clock = remoteClock;
            if(clock == null || clock.Handle != masterHandle)clock = new() { Handle=masterHandle, Position=PotPlayer.Read(masterHandle,20484)-target.Offset, At=now, State=PotPlayer.Read(masterHandle,20486), Speed=PotPlayer.Read(masterHandle,20501) };
            clock.Position = operation?.cur ?? clock.Now(now); clock.At = now; clock.Offset = target.Offset;
            clock.Duration = PotPlayer.Read(masterHandle,20482); clock.Peer = source;
            if(operation?.state is int state)clock.State = state;
            if(operation?.speed is int speed)clock.Speed = speed;
            clock.Boundary = PlaybackTimeline.Boundary(clock.Position,clock.Offset,clock.Duration); remoteClock = clock;
            if(target.Muted is bool muted) { PotPlayer.Mute(masterHandle,muted);actualMute[masterHandle]=muted; }
            if(operation?.speed is int rate)PotPlayer.Speed(masterHandle,rate);
            if(operation?.cur is int position) {
                PotPlayer.Prepare(masterHandle);
                PotPlayer.Align(masterHandle,[],clock.State,position,masterOffset:target.Offset,externalSource:true);
                var sample = PotPlayer.Read(masterHandle);
                if(clock.State == 2 && sample.State == 2) { clock.Position=sample.Position-clock.Offset;clock.At=sample.At; }
            } else if(operation?.state is int playback)PotPlayer.State(masterHandle,PlaybackTimeline.State(clock.Now(now),clock.Offset,clock.Duration,playback));
            ObserveSource();message = Localization.F("已接收 · {0}",peer.Name);
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
                        var sample = PotPlayer.Read(masterHandle); bool becameReady = masterDuration <= 0 && sample.Duration > 0; masterDuration = sample.Duration; var change = detector.Observe(sample, input.NavigationRecent); bool speedChanged = detector.SpeedChanged;
                        if (becameReady) Broadcast(Event(sample));
                        if (change.seek) input.Consume();
                        if (change.seek || change.state || speedChanged) Trace($"position={sample.Position} state={sample.State} speed={sample.Speed} event={change}");
                        if (syncing) {
                            bool aligned = localFollowers.Length > 0 && (change.seek || change.state && sample.State == 2);
                            if (aligned) {
                                long version = input.Version; AlignLocal(); AutomaticSeeks += localFollowers.Length;
                                sample = ObserveSource();
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
                            PublishPrimary(masterHandle);
                            lastMutePoll = sample.At;
                        }
                    } else { masterDuration = 0; playbackState = 0; readings = ""; if(IsRoutineMessage())message = Localization.T("打开视频后选择主窗口"); }
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
