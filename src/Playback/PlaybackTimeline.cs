namespace SyncPlayer;

static class PlaybackTimeline
{
    // Leave a small decoding guard before EOF, where PotPlayer may advance its playlist.
    public static int End(int duration) => Math.Max(0, duration - Math.Min(300, Math.Max(1, duration / 4)));
    public static int Position(double source, int offset, int duration) => (int)Math.Clamp(source + offset, 0, End(duration));
    public static int Boundary(double source, int offset, int duration) => source + offset < 0 ? -1 : source + offset >= End(duration) ? 1 : 0;
    public static int State(double source, int offset, int duration, int state) => state == 2 && Boundary(source, offset, duration) == 0 ? 2 : 1;
}
