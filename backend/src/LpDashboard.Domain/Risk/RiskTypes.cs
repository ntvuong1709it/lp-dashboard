namespace LpDashboard.Domain.Risk;

/// <summary>How bad one finding is. Order matters: higher is worse.</summary>
public enum RiskSeverity
{
    Info = 0,
    Yellow = 1,
    Red = 2,
}

/// <summary>The traffic light shown for a token or pool.</summary>
public enum RiskLevel
{
    /// <summary>No check exists (or none of its sources answered).</summary>
    NotChecked,

    /// <summary>A required source failed and nothing worse was found, so green would be a guess.</summary>
    Partial,
    Green,
    Yellow,
    Red,
}

/// <param name="ChecksRun">Sources that answered.</param>
/// <param name="ChecksTotal">Sources that were asked.</param>
public sealed record RiskVerdict(RiskLevel Level, int ChecksRun, int ChecksTotal)
{
    public static readonly RiskVerdict NotChecked = new(RiskLevel.NotChecked, 0, 0);

    /// <summary>
    /// Worst-of combination used for a pool (its own findings plus both tokens). Red beats Yellow; otherwise any component
    /// that is Partial or NotChecked makes the result Partial (NotChecked when every component is), and only all-Green is Green.
    /// </summary>
    public static RiskVerdict Combine(params RiskVerdict[] parts)
    {
        var run = parts.Sum(p => p.ChecksRun);
        var total = parts.Sum(p => p.ChecksTotal);
        if (parts.Any(p => p.Level == RiskLevel.Red)) return new(RiskLevel.Red, run, total);
        if (parts.Any(p => p.Level == RiskLevel.Yellow)) return new(RiskLevel.Yellow, run, total);
        if (parts.All(p => p.Level == RiskLevel.NotChecked)) return new(RiskLevel.NotChecked, run, total);
        if (parts.Any(p => p.Level is RiskLevel.Partial or RiskLevel.NotChecked)) return new(RiskLevel.Partial, run, total);
        return new(RiskLevel.Green, run, total);
    }
}

/// <param name="Code">Stable identifier, see <see cref="RiskCodes"/>.</param>
/// <param name="Evidence">The numbers behind the finding, readable as is.</param>
/// <param name="Source">Which source(s) said so, for the report.</param>
/// <param name="Subject">The address the finding is about (a holder, owner, implementation), when there is one.</param>
public sealed record RiskFinding(string Code, RiskSeverity Severity, string Title, string Evidence, string Source, string? Subject = null);

/// <param name="Name">One of <see cref="RiskSources"/>.</param>
/// <param name="Required">A required source that failed turns a clean result into Partial instead of Green.</param>
/// <param name="Answered">The source returned usable data.</param>
/// <param name="Detail">Why it did not answer ("HTTP 429", "timeout", "not configured").</param>
public sealed record SourceResult(string Name, bool Required, bool Answered, string? Detail = null);

public static class RiskSources
{
    public const string GoPlus = "goplus";
    public const string HoneypotIs = "honeypot";
    public const string Rpc = "rpc";
    public const string DexScreener = "dexscreener";
    public const string Etherscan = "etherscan";
    public const string Prices = "prices";
    public const string PoolStats = "poolstats";
    public const string LpIndex = "lpindex";

    public static string DisplayName(string source) => source switch
    {
        GoPlus => "GoPlus",
        HoneypotIs => "honeypot.is",
        Rpc => "on-chain (RPC)",
        DexScreener => "DexScreener",
        Etherscan => "Basescan",
        Prices => "DefiLlama prices",
        PoolStats => "pool explorer",
        LpIndex => "pool LP index",
        _ => source,
    };
}

public static class RiskCodes
{
    // Token
    public const string Honeypot = "HONEYPOT";
    public const string PrivilegedMintOrBlacklist = "PRIVILEGED_MINT_OR_BLACKLIST";
    public const string TopHolderConcentration = "TOP_HOLDER_CONCENTRATION";
    public const string Top10Concentration = "TOP10_CONCENTRATION";
    public const string SellTax = "SELL_TAX";
    public const string BuyTax = "BUY_TAX";
    public const string UnverifiedSource = "UNVERIFIED_SOURCE";
    public const string UpgradeableProxy = "UPGRADEABLE_PROXY";
    public const string OwnerNotRenounced = "OWNER_NOT_RENOUNCED";
    public const string TokenYoung = "TOKEN_YOUNG";
    public const string ThinLiquidity = "THIN_LIQUIDITY";
    public const string WashSuspect = "WASH_SUSPECT";
    public const string PriceHistoryMissing = "PRICE_HISTORY_MISSING";
    public const string StableDepeg = "STABLE_DEPEG";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string TrustedToken = "TRUSTED_TOKEN";

    // Pool
    public const string LpConcentration = "LP_CONCENTRATION";
    public const string PoolYoung = "POOL_YOUNG";
    public const string EmissionDependent = "EMISSION_DEPENDENT";
    public const string GaugeKilled = "GAUGE_KILLED";

    /// <summary>Codes that are bookkeeping rather than risk; they never show up as a "change".</summary>
    public static bool IsBookkeeping(string code) => code is SourceUnavailable or TrustedToken;

    /// <summary>
    /// The sources that can produce a finding. When none of them answered in a check, the finding's absence says nothing,
    /// so the change detector does not report it as cleared.
    /// </summary>
    public static IReadOnlyCollection<string> SourcesFor(string code) => code switch
    {
        Honeypot => [RiskSources.GoPlus, RiskSources.HoneypotIs],
        PrivilegedMintOrBlacklist => [RiskSources.GoPlus],
        TopHolderConcentration or Top10Concentration => [RiskSources.GoPlus],
        SellTax or BuyTax => [RiskSources.GoPlus, RiskSources.HoneypotIs],
        UnverifiedSource => [RiskSources.GoPlus, RiskSources.Etherscan],
        UpgradeableProxy => [RiskSources.Rpc, RiskSources.GoPlus],
        OwnerNotRenounced => [RiskSources.Rpc, RiskSources.GoPlus],
        TokenYoung => [RiskSources.DexScreener, RiskSources.Etherscan],
        ThinLiquidity or WashSuspect => [RiskSources.DexScreener],
        PriceHistoryMissing or StableDepeg => [RiskSources.Prices],
        LpConcentration => [RiskSources.LpIndex],
        PoolYoung => [RiskSources.DexScreener],
        EmissionDependent => [RiskSources.PoolStats],
        GaugeKilled => [RiskSources.Rpc],
        _ => [],
    };
}
