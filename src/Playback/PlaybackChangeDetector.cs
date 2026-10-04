namespace SyncPlayer;

// Only master observations generate events. Follower observations never feed back.
sealed class PlaybackChangeDetector
{
    public bool SpeedChanged { get; private set; }
    PlaybackSample? previous;
    PlaybackSample? lastPositionChange;
    bool alignFirst = true;
    public void Reset(bool align = true) { previous = null; lastPositionChange = null; alignFirst = align; }
    public (bool state, bool seek) Observe(PlaybackSample now, bool navigation = false)
    {
        var old = previous; previous = now;
        SpeedChanged = old != null && now.Speed != old.Speed;
        if (old == null) { lastPositionChange = now; return (alignFirst, alignFirst); }
        if (now.Position == old.Position) {
            if (now.State != old.State || SpeedChanged) lastPositionChange = now;
            return (now.State != old.State, false);
        }
        var anchor = lastPositionChange ?? old;
        lastPositionChange = now;
        double expected = anchor.State == 2 ? (now.At - anchor.At) * anchor.Speed : 0;
        bool changedState = now.State != old.State;
        int threshold = now.State != 2 ? 250 : navigation ? 100 : 1200;
        bool seek = now.Position - old.Position < -100 || !changedState && Math.Abs(now.Position - anchor.Position - expected) > threshold;
        return (changedState, seek);
    }
}
