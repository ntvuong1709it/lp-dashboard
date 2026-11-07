namespace LpDashboard.Application.Borrow;

/// <summary>Assets a manual loan can borrow or hold as collateral, with the DefiLlama price key used to value them.</summary>
public static class AssetCatalog
{
    public static readonly IReadOnlyList<AssetOptionDto> Assets =
    [
        new("BTC", "coingecko:bitcoin", false),
        new("ETH", "coingecko:ethereum", false),
        new("BNB", "coingecko:binancecoin", false),
        new("SOL", "coingecko:solana", false),
        new("USDT", "coingecko:tether", true),
        new("USDC", "coingecko:usd-coin", true),
        new("FDUSD", "coingecko:first-digital-usd", true),
        new("DAI", "coingecko:dai", true),
    ];

    public static string? PriceKeyFor(string? symbol) =>
        Assets.FirstOrDefault(a => a.Symbol.Equals(symbol?.Trim(), StringComparison.OrdinalIgnoreCase))?.PriceKey;
}
