using System.Numerics;
using LpDashboard.Domain;

namespace LpDashboard.Application.Abstractions;

public sealed record TokenInfo(string Address, string Symbol, int Decimals);

/// <summary>The NFT sat in <paramref name="Gauge"/> between the stake and unstake transfers (exclusive); open-ended while still staked.</summary>
public sealed record StakePeriod(string Gauge, long FromBlock, int FromLogIndex, long? ToBlock = null, int? ToLogIndex = null)
{
    public bool Covers(long block, int logIndex) =>
        (block, logIndex).CompareTo((FromBlock, FromLogIndex)) > 0
        && (ToBlock is null || (block, logIndex).CompareTo((ToBlock.Value, ToLogIndex ?? int.MaxValue)) < 0);
}

public sealed record DiscoveredPosition(
    string PositionManager,
    string TokenId,
    long FirstBlock,
    bool IsStaked,
    string? Gauge,
    bool IsBurned,
    // Null when the protocol has no staking or periods are unknown.
    IReadOnlyList<StakePeriod>? StakePeriods = null)
{
    public bool WasStakedIn(string gauge, long block, int logIndex) =>
        StakePeriods?.Any(s => s.Gauge.Equals(gauge, StringComparison.OrdinalIgnoreCase) && s.Covers(block, logIndex)) ?? false;
}

public sealed record PositionChainState(
    long BlockNumber,
    string PoolAddress,
    TokenInfo Token0,
    TokenInfo Token1,
    int? Fee,
    int TickSpacing,
    int TickLower,
    int TickUpper,
    BigInteger Liquidity,
    BigInteger SqrtPriceX96,
    int CurrentTick,
    // Everything collectable right now (fees + principal still owed).
    BigInteger Collectable0Raw,
    BigInteger Collectable1Raw,
    TokenInfo? RewardToken,
    BigInteger PendingRewardRaw,
    bool IsBurned);

public sealed record RawPositionEvent(
    string PositionManager,
    string TokenId,
    PositionEventType Type,
    string TxHash,
    int LogIndex,
    long BlockNumber,
    DateTime Timestamp,
    BigInteger Amount0Raw,
    BigInteger Amount1Raw,
    TokenInfo? RewardToken,
    BigInteger RewardRaw,
    string TxFrom,
    // Whole-tx fee in native token units (already divided by 1e18).
    decimal TxGasNative);

/// <summary>Sub-step update for long-running provider work; <paramref name="Total"/> is 0 when unknown.</summary>
public sealed record ProviderProgress(string Step, int Done, int Total);

/// <summary>One implementation per chain + protocol family.</summary>
public interface IPositionProvider
{
    Chain Chain { get; }
    Protocol Protocol { get; }
    string NativePriceKey { get; }
    string GetPriceKey(string tokenAddress);

    Task<long> GetLatestBlockAsync(CancellationToken ct);
    Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct);
    Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(
        string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null);

    /// <param name="knownTxHashes">Tx hashes of this position's events; used to resolve burned positions.</param>
    Task<PositionChainState> GetStateAsync(string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct);
}

public sealed record PricePoint(long Timestamp, decimal Price);

public interface IPriceSource
{
    Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct);
    Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct);

    /// <summary><paramref name="count"/> points every <paramref name="stepSeconds"/> (whole hours) from <paramref name="start"/>, in a single request where possible.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
        IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct);
}
