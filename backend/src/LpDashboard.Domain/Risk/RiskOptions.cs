namespace LpDashboard.Domain.Risk;

/// <summary>
/// Thresholds and the trusted-token list for the risk rules (config section <c>Risk</c>). Every default is a starting
/// point from docs/risk-screen-spec.md section 5 and can be tuned without code changes.
/// </summary>
public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    /// <summary>Largest holder above this share of supply is Red (after exclusions).</summary>
    public decimal TopHolderRedPct { get; set; } = 30m;

    /// <summary>Creator above this share of supply is Red (after exclusions).</summary>
    public decimal CreatorRedPct { get; set; } = 20m;

    /// <summary>Top 10 holders above this share of supply is Yellow (after exclusions).</summary>
    public decimal Top10YellowPct { get; set; } = 50m;

    /// <summary>Buy or sell tax from this level up is Yellow.</summary>
    public decimal TaxWarnPct { get; set; } = 3m;

    /// <summary>A sell tax above this is treated as a honeypot (Red).</summary>
    public decimal SellTaxHoneypotPct { get; set; } = 10m;

    /// <summary>Liquidity under this across the token's pools is Yellow.</summary>
    public decimal MinLiquidityUsd { get; set; } = 100_000m;

    /// <summary>Token or pool younger than this many days is Yellow.</summary>
    public int YoungDays { get; set; } = 7;

    /// <summary>24 h volume above this multiple of liquidity ...</summary>
    public decimal WashVolumeToLiquidity { get; set; } = 10m;

    /// <summary>... with fewer 24 h trades than this is a wash-trading suspect (Yellow).</summary>
    public int WashMaxTrades { get; set; } = 100;

    /// <summary>A stablecoin priced further than this from $1 is Yellow.</summary>
    public decimal StableDepegPct { get; set; } = 1m;

    /// <summary>One wallet's share of the active pool liquidity at or above this is Red.</summary>
    public decimal LpConcentrationRedPct { get; set; } = 50m;

    /// <summary>... at or above this is Yellow.</summary>
    public decimal LpConcentrationYellowPct { get; set; } = 30m;

    /// <summary>More than this share of a pool's APR coming from gauge emissions is Yellow.</summary>
    public decimal EmissionDependentPct { get; set; } = 80m;

    /// <summary>
    /// Well-known Base tokens, exempt from the privilege rules (USDC can blacklist by design). Honeypot and concentration
    /// checks still run for them. Addresses, case-insensitive. Null means <see cref="DefaultTrustedTokens"/>; setting it
    /// replaces the defaults (the config binder would otherwise append to a pre-filled list).
    /// </summary>
    public List<string>? TrustedTokens { get; set; }

    /// <summary>Default trusted tokens on Base. To be reviewed with the owner.</summary>
    public static readonly IReadOnlyList<string> DefaultTrustedTokens =
    [
        "0x4200000000000000000000000000000000000006", // WETH
        "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", // USDC
        "0xd9aAEc86B65D86f6A7B5B1b0c42FFA531710b6CA", // USDbC
        "0x50c5725949A6F0c72E6C4a641F24049A917DB0Cb", // DAI
        "0xcbB7C0000aB88B473b1f5aFd9ef808440eed33Bf", // cbBTC
        "0x2Ae3F1Ec7F1F5012CFEab0185bfc7aa3cf0DEc22", // cbETH
        "0xc1CBa3fCea344f92D9239c08C0568f6F2F0ee452", // wstETH
        "0x940181a94A35A4569E4529A3CDfB74e38FD98631", // AERO (v1)
        "0x3f5bf3edac59d650d8fdf9657a71f2f904a3a4ee", // AERO (current)
    ];

    public bool IsTrusted(string tokenAddress)
    {
        IEnumerable<string> trusted = TrustedTokens ?? (IEnumerable<string>)DefaultTrustedTokens;
        return trusted.Any(t => string.Equals(t?.Trim(), tokenAddress?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
