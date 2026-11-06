using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Metrics;

public enum AssetClass
{
    Stablecoin,
    Ether,
    Bitcoin,
    Other
}

/// <summary>Buckets a token by symbol so exposure can be read as "how much of this is ETH / BTC / dollars".</summary>
public static class AssetClassifier
{
    private static readonly HashSet<string> Stablecoins = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "USDbC", "USDT", "USD₮0", "DAI", "USDS", "sUSDS", "USDe", "crvUSD", "GHO", "LUSD", "FRAX", "USD+", "DOLA",
        "PYUSD", "FDUSD", "TUSD", "USDD", "USDz", "AUSD", "USR", "RLUSD", "USDtb", "USDG"
    };

    private static readonly HashSet<string> Ethers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ETH", "WETH", "cbETH", "wstETH", "stETH", "weETH", "rETH", "ezETH", "rsETH", "wrsETH", "OETH", "superOETHb", "swETH", "mETH"
    };

    private static readonly HashSet<string> Bitcoins = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTC", "WBTC", "cbBTC", "tBTC", "BTCB", "LBTC", "eBTC", "solvBTC"
    };

    public static AssetClass Classify(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return AssetClass.Other;
        var s = symbol.Trim();
        if (Stablecoins.Contains(s)) return AssetClass.Stablecoin;
        if (Ethers.Contains(s)) return AssetClass.Ether;
        if (Bitcoins.Contains(s)) return AssetClass.Bitcoin;
        return AssetClass.Other;
    }
}

/// <param name="ValueUsd">Principal currently inside open positions.</param>
/// <param name="UnclaimedUsd">Fees and rewards waiting to be claimed, held in this token.</param>
public sealed record TokenExposure(string Symbol, AssetClass AssetClass, decimal Amount, decimal ValueUsd, decimal UnclaimedUsd)
{
    public decimal TotalUsd => ValueUsd + UnclaimedUsd;
}

public sealed record AssetClassExposure(AssetClass AssetClass, decimal ValueUsd, decimal UnclaimedUsd)
{
    public decimal TotalUsd => ValueUsd + UnclaimedUsd;
}

/// <param name="Tokens">Largest first.</param>
/// <param name="HasMissingPrices">Some holdings had no price and are not counted, so the totals are understated.</param>
public sealed record PortfolioExposure(
    decimal TotalUsd,
    IReadOnlyList<TokenExposure> Tokens,
    IReadOnlyList<AssetClassExposure> Classes,
    bool HasMissingPrices);

/// <summary>
/// What the open positions actually hold, per token. A concentrated position is long the volatile token it is
/// converted into as price falls, so the amounts (not the pair names) show the real directional risk. Unclaimed fees
/// and pending rewards are included, in their own token, but reported separately from principal.
/// </summary>
public static class PortfolioExposureCalculator
{
    private sealed record Holding(string Symbol, decimal Amount, decimal Value, decimal Unclaimed);

    public static PortfolioExposure Calculate(IEnumerable<Position> positions)
    {
        var byToken = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        var missing = false;

        void Add(string? symbol, decimal principalAmount, decimal unclaimedAmount, decimal? price)
        {
            if (principalAmount == 0 && unclaimedAmount == 0) return;
            if (price is null)
            {
                missing = true;
                return;
            }

            var key = string.IsNullOrWhiteSpace(symbol) ? "?" : symbol.Trim();
            var cur = byToken.GetValueOrDefault(key) ?? new Holding(key, 0, 0, 0);
            byToken[key] = new Holding(
                cur.Symbol,
                cur.Amount + principalAmount + unclaimedAmount,
                cur.Value + principalAmount * price.Value,
                cur.Unclaimed + unclaimedAmount * price.Value);
        }

        foreach (var p in positions.Where(p => p.Status == PositionStatus.Open))
        {
            Add(p.Pool?.Token0?.Symbol, p.CurrentAmount0, p.Uncollected0, p.Price0Usd);
            Add(p.Pool?.Token1?.Symbol, p.CurrentAmount1, p.Uncollected1, p.Price1Usd);
            Add(p.RewardTokenSymbol, 0, p.PendingReward, p.RewardPriceUsd);
        }

        var tokens = byToken.Values
            .Select(v => new TokenExposure(v.Symbol, AssetClassifier.Classify(v.Symbol), v.Amount, v.Value, v.Unclaimed))
            .OrderByDescending(t => t.TotalUsd)
            .ToList();

        var classes = tokens
            .GroupBy(t => t.AssetClass)
            .Select(g => new AssetClassExposure(g.Key, g.Sum(t => t.ValueUsd), g.Sum(t => t.UnclaimedUsd)))
            .OrderByDescending(c => c.TotalUsd)
            .ToList();

        return new PortfolioExposure(tokens.Sum(t => t.TotalUsd), tokens, classes, missing);
    }
}
