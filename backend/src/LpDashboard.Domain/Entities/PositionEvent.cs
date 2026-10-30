namespace LpDashboard.Domain.Entities;

public class PositionEvent
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public Position? Position { get; set; }
    public PositionEventType Type { get; set; }
    public string TxHash { get; set; } = string.Empty;
    public int LogIndex { get; set; }
    public long BlockNumber { get; set; }
    public DateTime Timestamp { get; set; }

    public decimal Amount0 { get; set; }
    public decimal Amount1 { get; set; }
    public decimal Principal0 { get; set; }
    public decimal Principal1 { get; set; }
    public decimal Fee0 { get; set; }
    public decimal Fee1 { get; set; }

    public string? RewardTokenAddress { get; set; }
    public string? RewardTokenSymbol { get; set; }
    public decimal RewardAmount { get; set; }

    public decimal? Price0Usd { get; set; }
    public decimal? Price1Usd { get; set; }
    public decimal? RewardPriceUsd { get; set; }

    /// <summary>This position's share of the tx fee, in the chain's native token.</summary>
    public decimal GasNative { get; set; }
    public decimal? GasUsd { get; set; }
}
