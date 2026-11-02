using System.Numerics;

namespace LpLedger.Core;

/// <summary>A position NFT. Token IDs repeat across the several position managers Aerodrome has deployed.</summary>
public sealed record PositionRef(string Manager, BigInteger TokenId)
{
    public string Key => $"{Manager.ToLowerInvariant()}:{TokenId}";
}

/// <summary>Burned is true when the NFT no longer exists and the data was read from the block before the burn.</summary>
public sealed record RawPosition(
    string Token0, string Token1, int TickSpacing, int TickLower, int TickUpper, BigInteger Liquidity, bool Burned = false);

public sealed record RawLiquidityLog(
    Models.EventKind Kind, PositionRef Ref, BigInteger Amount0, BigInteger Amount1,
    long BlockNumber, string TxHash, int LogIndex);

public sealed record Slot0(BigInteger SqrtPriceX96, int Tick);

public sealed record TokenMeta(string Symbol, int Decimals);

/// <summary>Everything the Aerodrome adapter needs from the chain. A fake implements this in tests and demo mode.</summary>
public interface IChainReader
{
    /// <summary>Every position NFT the wallet has ever received or sent. Includes ones now staked in a gauge or burned.</summary>
    Task<IReadOnlyList<PositionRef>> CandidatesAsync(string owner);
    /// <summary>Add, remove and collect events for the given positions, in the context of one wallet.</summary>
    Task<IReadOnlyList<RawLiquidityLog>> LiquidityLogsAsync(string owner, IReadOnlyList<PositionRef> refs);
    Task<IReadOnlyDictionary<long, long>> BlockTimestampsAsync(IReadOnlyCollection<long> blocks);
    /// <summary>Null when the position cannot be read, including a burned NFT whose pre-burn state is unavailable.</summary>
    Task<RawPosition?> PositionAsync(PositionRef position);
    /// <summary>Null when the NFT was burned.</summary>
    Task<string?> OwnerOfAsync(PositionRef position);
    Task<string> PoolAsync(string manager, string token0, string token1, int tickSpacing);
    Task<Slot0> Slot0Async(string pool);
    /// <summary>Zero address when the pool has no gauge.</summary>
    Task<string> GaugeAsync(string pool);
    Task<bool> StakedContainsAsync(string gauge, string owner, BigInteger tokenId);
    Task<BigInteger> EarnedAeroAsync(string gauge, string owner, BigInteger tokenId);
    /// <summary>Uncollected trading fees plus any tokens owed, via a simulated collect().</summary>
    Task<(BigInteger Amount0, BigInteger Amount1)> PendingFeesAsync(string owner, PositionRef position);
    Task<TokenMeta> TokenAsync(string address);
}

/// <summary>USD prices. Keys in the returned maps are lowercase token addresses.</summary>
public interface IPriceSource
{
    Task<IReadOnlyDictionary<string, decimal>> CurrentAsync(IEnumerable<string> addresses);
    Task<IReadOnlyDictionary<string, decimal>> AtAsync(long timestamp, IEnumerable<string> addresses);
}
