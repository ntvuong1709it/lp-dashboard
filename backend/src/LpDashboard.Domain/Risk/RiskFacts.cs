namespace LpDashboard.Domain.Risk;

// Plain records of everything collected for a token or pool. They are stored as JSON next to the verdict so a rule
// change can re-evaluate old facts. Every nested block is null when its source did not answer; every nullable field is
// null when the source did not say. Percentages are 0-100.

/// <param name="Address">Holder address.</param>
/// <param name="SharePct">Share of total supply, 0-100.</param>
/// <param name="IsPool">The adapter recognised this holder as a liquidity pool or router (GoPlus tag).</param>
/// <param name="IsLocked">Tokens are locked or vested in a locker contract.</param>
public sealed record HolderFact(string Address, decimal SharePct, bool IsContract, bool IsLocked, bool IsPool, string? Tag = null);

public sealed record GoPlusFacts(
    bool? IsHoneypot,
    decimal? BuyTaxPct,
    decimal? SellTaxPct,
    bool? CanMint,
    bool? CanBlacklist,
    bool? CanPauseTransfers,
    string? OwnerAddress,
    string? CreatorAddress,
    decimal? CreatorSharePct,
    bool? IsOpenSource,
    bool? IsProxy,
    int? HolderCount,
    IReadOnlyList<HolderFact>? Holders,
    string? Symbol = null);

public sealed record HoneypotIsFacts(
    bool? IsHoneypot,
    bool? SimulationFailed,
    decimal? BuyTaxPct,
    decimal? SellTaxPct,
    string? Reason);

/// <summary>Facts read from the node: <c>owner()</c>, the EIP-1967 slots and <c>eth_getCode</c>.</summary>
/// <param name="OwnerAddress">Result of <c>owner()</c>; null when the token has no such function. The zero address means renounced.</param>
/// <param name="ProxyImplementation">EIP-1967 implementation slot; null or zero when the token is not a proxy.</param>
public sealed record OnChainFacts(
    string? OwnerAddress,
    bool? OwnerIsContract,
    string? ProxyImplementation,
    string? ProxyAdmin,
    bool? ProxyAdminIsContract);

/// <summary>Market facts across the token's pools (DexScreener).</summary>
/// <param name="FirstPoolCreatedAt">Creation time of the token's oldest pool.</param>
/// <param name="PoolAddresses">Pools the token trades in; holders at these addresses are not counted as concentration.</param>
public sealed record MarketFacts(
    decimal? LiquidityUsd,
    decimal? Volume24hUsd,
    int? Trades24h,
    DateTime? FirstPoolCreatedAt,
    IReadOnlyList<string> PoolAddresses);

/// <summary>Verified-source flag and creation date from Basescan (optional source, needs a key).</summary>
public sealed record ContractFacts(bool? SourceVerified, DateTime? CreatedAt);

public sealed record PriceFacts(decimal? PriceUsd, bool? HasHistory);

/// <param name="CollectedAt">UTC time the facts were collected; the evaluator uses it as "now" so evaluation stays pure.</param>
public sealed record TokenRiskFacts(
    string TokenAddress,
    string? Symbol,
    DateTime CollectedAt,
    IReadOnlyList<SourceResult> Sources,
    GoPlusFacts? GoPlus = null,
    HoneypotIsFacts? HoneypotIs = null,
    OnChainFacts? OnChain = null,
    MarketFacts? Market = null,
    ContractFacts? Contract = null,
    PriceFacts? Prices = null)
{
    public bool Answered(string source) => Sources.Any(s => s.Name == source && s.Answered);
}

/// <summary>One wallet's share of a pool's active liquidity, from the pool LP index (docs/smart-lp-spec.md).</summary>
/// <param name="Partial">The index does not reach the pool's first block, so the share may be understated.</param>
public sealed record LpConcentrationFacts(string TopHolderAddress, decimal TopSharePct, bool Partial);

/// <param name="FeeAprPct">Trailing fee APR.</param>
/// <param name="RewardAprPct">Gauge emission APR; null or zero for pools without a gauge.</param>
/// <param name="GaugeAlive">Null when the pool has no gauge or the check did not run.</param>
/// <param name="LpConcentration">Null until the pool LP index exists: the finding then reads "not checked".</param>
public sealed record PoolRiskFacts(
    string PoolAddress,
    DateTime CollectedAt,
    IReadOnlyList<SourceResult> Sources,
    DateTime? PoolCreatedAt = null,
    decimal? FeeAprPct = null,
    decimal? RewardAprPct = null,
    bool? GaugeAlive = null,
    LpConcentrationFacts? LpConcentration = null)
{
    public bool Answered(string source) => Sources.Any(s => s.Name == source && s.Answered);
}

/// <summary>The address that can change a token's rules: <c>owner()</c> or, failing that, the proxy admin.</summary>
/// <param name="IsContract">Controlled by code (assumed multisig or timelock); null when unknown.</param>
public sealed record TokenController(string Address, bool? IsContract, bool IsProxyAdmin);

public static class RiskAddresses
{
    public const string Zero = "0x0000000000000000000000000000000000000000";
    public const string Dead = "0x000000000000000000000000000000000000dead";

    public static bool IsZero(string? address) =>
        string.IsNullOrWhiteSpace(address) || string.Equals(address.Trim(), Zero, StringComparison.OrdinalIgnoreCase);

    /// <summary>The zero address and the conventional burn address.</summary>
    public static bool IsBurn(string? address) =>
        IsZero(address) || string.Equals(address!.Trim(), Dead, StringComparison.OrdinalIgnoreCase);

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Derived views of the facts that both the evaluator and the change detector need.</summary>
public static class RiskFactsAnalysis
{
    /// <summary>
    /// Owner first (RPC, then GoPlus), then the proxy admin. Null when there is nobody: renounced, or no owner function at all.
    /// </summary>
    public static TokenController? Controller(TokenRiskFacts f)
    {
        var rpcOwner = f.OnChain?.OwnerAddress;
        if (!RiskAddresses.IsZero(rpcOwner)) return new TokenController(rpcOwner!, f.OnChain!.OwnerIsContract, false);

        var goPlusOwner = f.GoPlus?.OwnerAddress;
        if (!RiskAddresses.IsZero(goPlusOwner))
            return new TokenController(goPlusOwner!, Same(f.OnChain?.OwnerAddress, goPlusOwner) ? f.OnChain!.OwnerIsContract : null, false);

        var admin = f.OnChain?.ProxyAdmin;
        if (!RiskAddresses.IsZero(f.OnChain?.ProxyImplementation) && !RiskAddresses.IsZero(admin))
            return new TokenController(admin!, f.OnChain!.ProxyAdminIsContract, true);

        return null;

        static bool Same(string? a, string? b) => RiskAddresses.Same(a, b);
    }

    /// <summary>True when <c>owner()</c> exists and returned the zero address.</summary>
    public static bool OwnerRenounced(TokenRiskFacts f) =>
        f.OnChain?.OwnerAddress is { } owner && RiskAddresses.IsZero(owner) && !string.IsNullOrWhiteSpace(owner);

    public static string? Implementation(TokenRiskFacts f) =>
        RiskAddresses.IsZero(f.OnChain?.ProxyImplementation) ? null : f.OnChain!.ProxyImplementation;

    /// <summary>
    /// Holders that do not count towards concentration: pools and routers, the burn addresses, the token itself,
    /// locked or vested holders, and the pools DexScreener lists for the token.
    /// </summary>
    public static bool IsExcludedHolder(TokenRiskFacts f, HolderFact h) =>
        h.IsPool
        || h.IsLocked
        || RiskAddresses.IsBurn(h.Address)
        || RiskAddresses.Same(h.Address, f.TokenAddress)
        || (f.Market?.PoolAddresses.Any(p => RiskAddresses.Same(p, h.Address)) ?? false);

    /// <summary>The largest holder that counts, or null when GoPlus gave no holder list or nobody is left.</summary>
    public static HolderFact? TopHolder(TokenRiskFacts f) =>
        f.GoPlus?.Holders?.Where(h => !IsExcludedHolder(f, h)).OrderByDescending(h => h.SharePct).FirstOrDefault();

    /// <summary>Combined share of the top 10 holders (GoPlus lists only 10) that count; null without a holder list.</summary>
    public static decimal? Top10Share(TokenRiskFacts f) =>
        f.GoPlus?.Holders is { } holders
            ? holders.OrderByDescending(h => h.SharePct).Take(10).Where(h => !IsExcludedHolder(f, h)).Sum(h => h.SharePct)
            : null;

    /// <summary>The creator's share, unless the creator is an excluded address (burned, a pool, locked).</summary>
    public static decimal? CreatorShare(TokenRiskFacts f)
    {
        var gp = f.GoPlus;
        if (gp?.CreatorSharePct is not { } share) return null;
        if (RiskAddresses.IsBurn(gp.CreatorAddress) || RiskAddresses.Same(gp.CreatorAddress, f.TokenAddress)) return null;
        var asHolder = gp.Holders?.FirstOrDefault(h => RiskAddresses.Same(h.Address, gp.CreatorAddress));
        return asHolder is not null && IsExcludedHolder(f, asHolder) ? null : share;
    }
}
