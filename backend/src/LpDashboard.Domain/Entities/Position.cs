using System.Numerics;

namespace LpDashboard.Domain.Entities;

public class Position
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public Protocol Protocol { get; set; }
    public string PositionManager { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int? PoolId { get; set; }
    public Pool? Pool { get; set; }
    public int TickLower { get; set; }
    public int TickUpper { get; set; }
    public BigInteger Liquidity { get; set; }
    public PositionStatus Status { get; set; }
    public bool IsStaked { get; set; }
    public string? Gauge { get; set; }
    public DateTime? OpenedAt { get; set; }
    public long? OpenedBlock { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Latest valuation (denormalized from the last sync); history lives in PositionSnapshot.
    public int? CurrentTick { get; set; }
    public BigInteger? SqrtPriceX96 { get; set; }
    public decimal CurrentAmount0 { get; set; }
    public decimal CurrentAmount1 { get; set; }
    public decimal Uncollected0 { get; set; }
    public decimal Uncollected1 { get; set; }
    public decimal PendingReward { get; set; }
    public string? RewardTokenAddress { get; set; }
    public string? RewardTokenSymbol { get; set; }
    public decimal? Price0Usd { get; set; }
    public decimal? Price1Usd { get; set; }
    public decimal? RewardPriceUsd { get; set; }
    public DateTime? ValuedAt { get; set; }

    public List<PositionEvent> Events { get; set; } = [];
}
