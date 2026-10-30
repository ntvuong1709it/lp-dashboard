namespace LpDashboard.Domain.Entities;

public class TokenPrice
{
    public int Id { get; set; }
    /// <summary>Price source key, e.g. "base:0xabc..." or "coingecko:ethereum".</summary>
    public string PriceKey { get; set; } = string.Empty;
    /// <summary>Unix seconds, bucketed.</summary>
    public long Timestamp { get; set; }
    public decimal? PriceUsd { get; set; }
}
