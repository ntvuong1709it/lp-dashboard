using LpDashboard.Domain;
using LpDashboard.Domain.Risk;

namespace LpDashboard.Application.Risk;

/// <summary>
/// What a source returned: a value, or the reason it did not answer. Sources never throw for provider failures
/// (rate limit, timeout, bad payload); they return a failure so a dead source degrades to "check unavailable".
/// </summary>
public sealed record SourceOutcome<T>(T? Value, string? Failure) where T : class
{
    /// <summary>The source gave a complete answer. A partial one has a value but does not count as a check that ran.</summary>
    public bool Answered => Value is not null && Failure is null;

    public static SourceOutcome<T> Ok(T value) => new(value, null);

    public static SourceOutcome<T> Fail(string reason) => new(null, reason);

    /// <summary>Usable but incomplete data (a key flag is missing): its findings are kept, the check is reported as not run.</summary>
    public static SourceOutcome<T> Partial(T value, string reason) => new(value, reason);
}

/// <summary>GoPlus Security API: flags, taxes, owner, creator, top holders.</summary>
public interface IGoPlusSource
{
    Task<SourceOutcome<GoPlusFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct);
}

/// <summary>honeypot.is: a simulated buy and sell.</summary>
public interface IHoneypotSource
{
    Task<SourceOutcome<HoneypotIsFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct);
}

/// <summary>
/// What the node says about a token. An address that holds no contract is reported as a failure with
/// <see cref="NoContract"/> as the reason, not as an empty reading.
/// </summary>
public sealed record OnChainReading(OnChainFacts Facts, string? Symbol)
{
    public const string NoContract = "no contract at this address";
}

public sealed record GaugeStatus(bool HasGauge, bool? Alive);

/// <summary>The node: <c>owner()</c>, EIP-1967 slots, <c>eth_getCode</c>, symbol and the Aerodrome gauge state.</summary>
public interface IOnChainFactsSource
{
    bool Supports(Chain chain);

    Task<SourceOutcome<OnChainReading>> GetTokenAsync(Chain chain, string token, CancellationToken ct);

    /// <summary>Asks the pool for its gauge and the voter whether that gauge is alive. For Aerodrome Slipstream pools.</summary>
    Task<SourceOutcome<GaugeStatus>> GetGaugeStatusAsync(Chain chain, string pool, CancellationToken ct);
}

/// <param name="CreatedAt">The pool's creation time.</param>
public sealed record PoolMarketFacts(DateTime? CreatedAt, decimal? LiquidityUsd);

/// <summary>DexScreener: pair age, liquidity and volume across the token's pools.</summary>
public interface IMarketFactsSource
{
    Task<SourceOutcome<MarketFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct);

    Task<SourceOutcome<PoolMarketFacts>> GetPoolAsync(Chain chain, string pool, CancellationToken ct);
}

/// <summary>Basescan/Etherscan: verified source and creation date. Optional, needs a key.</summary>
public interface IContractInfoSource
{
    bool IsConfigured { get; }

    Task<SourceOutcome<ContractFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct);
}
