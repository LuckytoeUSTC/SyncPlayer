namespace SyncPlayer;

record PlayerWindow(nint Handle, string Title)
{
    public string Name => System.Text.RegularExpressions.Regex.Replace(Title, @"\s*[-–·]\s*PotPlayer\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    public string DisplayName { get; init; } = "";
    public override string ToString() => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
}
record PlaybackSample(int Position, int Duration, int State, double At, int Speed = 1000);

