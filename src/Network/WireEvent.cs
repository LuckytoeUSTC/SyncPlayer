namespace SyncPlayer;

sealed record WireEvent
{
    public string type { get; init; } = "progress";
    public int? cur { get; init; }
    public int? total { get; init; }
    public int? state { get; init; }
    public int? speed { get; init; }
    public string? id { get; init; }
    public long seq { get; init; }
}

