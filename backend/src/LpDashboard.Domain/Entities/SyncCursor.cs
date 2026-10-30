namespace LpDashboard.Domain.Entities;

public class SyncCursor
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public Protocol Protocol { get; set; }
    public string Owner { get; set; } = string.Empty;
    public long LastSyncedBlock { get; set; }
    public DateTime UpdatedAt { get; set; }
}
