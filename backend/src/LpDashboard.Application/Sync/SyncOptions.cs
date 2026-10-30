namespace LpDashboard.Application.Sync;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public int IntervalMinutes { get; set; } = 15;
    public int SnapshotIntervalMinutes { get; set; } = 60;
    public bool RunOnStartup { get; set; } = true;
}
