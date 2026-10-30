namespace LpDashboard.Domain.Entities;

public class Pool
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public Protocol Protocol { get; set; }
    public string Address { get; set; } = string.Empty;
    public int Token0Id { get; set; }
    public Token Token0 { get; set; } = null!;
    public int Token1Id { get; set; }
    public Token Token1 { get; set; } = null!;
    /// <summary>Fee in hundredths of a bip (500 = 0.05%).</summary>
    public int? Fee { get; set; }
    public int TickSpacing { get; set; }
}
