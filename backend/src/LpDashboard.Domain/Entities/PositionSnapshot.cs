namespace LpDashboard.Domain.Entities;

public class PositionSnapshot
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public Position? Position { get; set; }
    public DateTime Timestamp { get; set; }
    public long BlockNumber { get; set; }
    public int CurrentTick { get; set; }
    public decimal Amount0 { get; set; }
    public decimal Amount1 { get; set; }
    public decimal Uncollected0 { get; set; }
    public decimal Uncollected1 { get; set; }
    public decimal PendingReward { get; set; }
    public decimal? Price0Usd { get; set; }
    public decimal? Price1Usd { get; set; }
    public decimal? RewardPriceUsd { get; set; }
    public decimal ValueUsd { get; set; }
    public decimal UncollectedUsd { get; set; }
}
