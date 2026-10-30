namespace LpDashboard.Domain.Entities;

public class WatchedAddress
{
    public int Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Label { get; set; }
    public ChainFamily ChainFamily { get; set; }
    public DateTime CreatedAt { get; set; }
}
