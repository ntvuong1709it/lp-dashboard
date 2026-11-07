using LpDashboard.Domain;

namespace LpDashboard.Application.Abstractions;

/// <summary>One Aave reserve the wallet holds or owes right now. Rates are percent per year; indices are the reserve's growth factors (about 1.0x).</summary>
public sealed record LendingReserveState(
    string Reserve,
    string Symbol,
    int Decimals,
    string PriceKey,
    decimal Supplied,
    decimal Borrowed,
    decimal SupplyAprPercent,
    decimal BorrowAprPercent,
    decimal SupplyIndex,
    decimal BorrowIndex,
    bool IsCollateral);

/// <summary>Aave's own view of the wallet. <see cref="HealthFactor"/> is null without debt.</summary>
public sealed record LendingAccountState(
    long Block,
    decimal TotalCollateralUsd,
    decimal TotalDebtUsd,
    decimal LiquidationThreshold,
    decimal? HealthFactor,
    IReadOnlyList<LendingReserveState> Reserves);

/// <summary>One reserve of one wallet at the end of a UTC day.</summary>
public sealed record LendingDayState(
    DateOnly Date,
    string Reserve,
    string Symbol,
    int Decimals,
    string PriceKey,
    decimal Supplied,
    decimal Borrowed,
    decimal SupplyIndex,
    decimal BorrowIndex,
    bool IsCollateral);

public sealed record LendingActivity(DateOnly FirstDay, IReadOnlyList<string> Reserves);

/// <summary>History could not be read for the days listed in <see cref="Failed"/>; <see cref="Note"/> says why.</summary>
public sealed record LendingHistory(IReadOnlyList<LendingDayState> Days, IReadOnlyList<DateOnly> Failed, string? Note);

/// <summary>Reads a lending market (Aave v3) for a wallet. One implementation per chain.</summary>
public interface ILendingProvider
{
    Chain Chain { get; }

    /// <summary>"Aave v3".</summary>
    string Market { get; }

    /// <summary>The wallet's account and reserves at the latest block; null when it has never used the market.</summary>
    Task<LendingAccountState?> GetAccountAsync(string owner, CancellationToken ct);

    /// <summary>
    /// First UTC day the wallet supplied or borrowed and every reserve it ever touched, from the market's events;
    /// null when that could not be read (e.g. the RPC limits log ranges).
    /// </summary>
    Task<LendingActivity?> FindActivityAsync(string owner, CancellationToken ct);

    /// <summary>
    /// End-of-day state of <paramref name="reserves"/> for each day in the range (inclusive; days before today only).
    /// Needs an RPC that serves historical state; days it cannot read are reported, not skipped silently.
    /// </summary>
    Task<LendingHistory> GetHistoryAsync(
        string owner,
        IReadOnlyCollection<string> reserves,
        DateOnly from,
        DateOnly to,
        CancellationToken ct,
        IProgress<ProviderProgress>? progress = null);
}
