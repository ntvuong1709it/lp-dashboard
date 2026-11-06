using LpDashboard.Domain;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Application.Queries;

public sealed record TokenDto(string Address, string Symbol, int Decimals);

/// <summary>Prices are token0 denominated in token1.</summary>
public sealed record RangeDto(
    double PriceLower,
    double PriceUpper,
    double? PriceCurrent,
    bool? InRange,
    double? DistanceToLowerPercent,
    double? DistanceToUpperPercent);

public sealed record PositionDto(
    int Id,
    Chain Chain,
    Protocol Protocol,
    string TokenId,
    string Owner,
    PositionStatus Status,
    bool IsStaked,
    string? PoolAddress,
    int? Fee,
    int? TickSpacing,
    TokenDto? Token0,
    TokenDto? Token1,
    int TickLower,
    int TickUpper,
    int? CurrentTick,
    RangeDto? Range,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    decimal Amount0,
    decimal Amount1,
    decimal Uncollected0,
    decimal Uncollected1,
    decimal PendingReward,
    string? RewardTokenSymbol,
    decimal? Price0Usd,
    decimal? Price1Usd,
    DateTime? ValuedAt,
    PositionMetrics Metrics,
    /// <summary>Measured from snapshots, completed with an estimate from historical prices where there are none. Null when nothing could be covered.</summary>
    TimeInRange? TimeInRange);

/// <param name="Estimated">At least one position's value at this time is reconstructed from events and prices, not measured.</param>
public sealed record PortfolioHistoryPointDto(DateTime Timestamp, decimal ValueUsd, decimal UnclaimedUsd, int PositionCount, bool Estimated);

public sealed record PnlPointDto(DateTime Timestamp, decimal CumulativePnlUsd);

public sealed record MonthlyPnlDto(string Month, decimal NetPnlUsd);

public sealed record PnlHistoryDto(IReadOnlyList<PnlPointDto> Points, IReadOnlyList<MonthlyPnlDto> Months);

public sealed record PositionEventDto(
    int Id,
    PositionEventType Type,
    string TxHash,
    long BlockNumber,
    DateTime Timestamp,
    decimal Amount0,
    decimal Amount1,
    decimal Principal0,
    decimal Principal1,
    decimal Fee0,
    decimal Fee1,
    string? RewardTokenSymbol,
    decimal RewardAmount,
    decimal? Price0Usd,
    decimal? Price1Usd,
    decimal? RewardPriceUsd,
    decimal? ValueUsd,
    decimal? GasUsd);

/// <summary>A position event with enough context to list it across positions.</summary>
public sealed record ActivityEventDto(
    int PositionId,
    Chain Chain,
    Protocol Protocol,
    string TokenId,
    string Pair,
    PositionEventDto Event);

public sealed record PositionDetailDto(PositionDto Position, IReadOnlyList<PositionEventDto> Events);

public sealed record PortfolioSummaryDto(
    int OpenCount,
    int ClosedCount,
    decimal CurrentValueUsd,
    decimal DepositedUsd,
    decimal FeesEarnedUsd,
    decimal UncollectedFeesUsd,
    decimal PendingRewardsUsd,
    decimal PnlUsd,
    decimal PnlVsHodlUsd,
    decimal ImpermanentLossUsd,
    decimal GasUsd,
    bool HasMissingPrices);

/// <summary>Portfolio-wide views that need more than one position at a time: what is held per token, and how reward tokens fared after being claimed.</summary>
public sealed record PortfolioInsightsDto(PortfolioExposure Exposure, IReadOnlyList<RewardTokenSummary> Rewards);

/// <param name="InRangeAprPercent">Lifetime rate per day spent in range.</param>
/// <param name="Apr7d">Open positions older than a week only.</param>
/// <param name="Apr30d">Open positions older than 30 days only.</param>
/// <param name="VsPool">Null when the analytics source does not list the pool.</param>
/// <param name="BreakEven">Open positions only.</param>
public sealed record PositionPerformanceDto(
    int PositionId,
    decimal? InRangeAprPercent,
    TrailingApr? Apr7d,
    TrailingApr? Apr30d,
    PoolComparison? VsPool,
    HodlBreakEven? BreakEven);

/// <param name="Portfolio">All positions' LP-vs-HODL gap against the combined earning rate of the open ones.</param>
public sealed record PortfolioPerformanceDto(IReadOnlyList<PositionPerformanceDto> Positions, HodlBreakEven? Portfolio);

public sealed record SnapshotPointDto(
    DateTime Timestamp,
    decimal ValueUsd,
    decimal ValueWithFeesUsd,
    decimal HodlValueUsd,
    decimal? Price0Usd,
    decimal? Price1Usd,
    int CurrentTick);

public sealed record FeeClaimItemDto(
    int PositionId,
    string TokenId,
    Protocol Protocol,
    string Pair,
    PositionEventType Type,
    string TxHash,
    DateTime Timestamp,
    decimal Fee0,
    decimal Fee1,
    string? Token0Symbol,
    string? Token1Symbol,
    decimal RewardAmount,
    string? RewardTokenSymbol,
    decimal FeesUsd,
    decimal RewardsUsd,
    decimal TotalUsd,
    bool HasMissingPrices);

/// <summary>Earned on a local day, whether or not it has been claimed yet.</summary>
public sealed record DailyEarningsDto(
    DateOnly Date,
    decimal FeesUsd,
    decimal RewardsUsd,
    decimal TotalUsd,
    bool HasMissingPrices);

public sealed record WeeklyClaimsDto(
    DateOnly WeekStart,
    decimal FeesUsd,
    decimal RewardsUsd,
    decimal TotalUsd,
    int ClaimCount,
    IReadOnlyList<FeeClaimItemDto> Items);
