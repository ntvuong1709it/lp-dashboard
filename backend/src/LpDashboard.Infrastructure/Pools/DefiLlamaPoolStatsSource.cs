using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LpDashboard.Application.Pools;
using LpDashboard.Domain;

namespace LpDashboard.Infrastructure.Pools;

/// <summary>
/// DefiLlama yields API (free, no key): one call returns every pool, which is filtered here to the supported protocols
/// on one chain. Pools are identified by DefiLlama's own id, so the token pair and fee tier are what link a pool to ours.
/// </summary>
internal sealed partial class DefiLlamaPoolStatsSource(HttpClient http) : IPoolStatsSource
{
    private static readonly Dictionary<Chain, string> ChainNames = new() { [Chain.Base] = "Base", [Chain.Ethereum] = "Ethereum" };

    private static readonly Dictionary<string, Protocol> Projects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uniswap-v3"] = Protocol.UniswapV3,
        ["aerodrome-slipstream"] = Protocol.AerodromeSlipstream,
    };

    public async Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct)
    {
        if (!ChainNames.TryGetValue(chain, out var chainName)) return [];

        using var response = await http.GetAsync("pools", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<PoolStats>();
        if (!document.RootElement.TryGetProperty("data", out var data)) return result;

        foreach (var item in data.EnumerateArray())
        {
            if (Str(item, "chain") != chainName) continue;
            if (Str(item, "project") is not { } project || !Projects.TryGetValue(project, out var protocol)) continue;
            if (Str(item, "pool") is not { } id) continue;
            if (!item.TryGetProperty("underlyingTokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array || tokens.GetArrayLength() != 2) continue;

            var (feePercent, tickSpacing) = ParseMeta(Str(item, "poolMeta"));
            if (Num(item, "tvlUsd") is not { } tvl || tvl <= 0) continue;

            result.Add(new PoolStats(
                id, chain, protocol, Str(item, "symbol") ?? "?",
                tokens[0].GetString()!.ToLowerInvariant(), tokens[1].GetString()!.ToLowerInvariant(),
                feePercent, tickSpacing, tvl, Num(item, "volumeUsd1d"), Num(item, "volumeUsd7d"),
                Num(item, "apyBase"), Num(item, "apyReward")));
        }
        return result;
    }

    public async Task<IReadOnlyList<PoolChartPoint>> GetChartAsync(string poolId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"chart/{Uri.EscapeDataString(poolId)}", ct);
        if (!response.IsSuccessStatusCode) return [];
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseChart(document.RootElement);
    }

    /// <summary>One point a day, oldest first; only the last 90 are kept. Points without a date or TVL are dropped.</summary>
    internal static IReadOnlyList<PoolChartPoint> ParseChart(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

        var points = new List<PoolChartPoint>();
        foreach (var p in data.EnumerateArray())
        {
            if (Str(p, "timestamp") is not { } ts
                || !DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
                || Num(p, "tvlUsd") is not { } tvl) continue;
            points.Add(new PoolChartPoint(date, tvl, Num(p, "apyBase"), Num(p, "apyReward")));
        }
        return points.OrderBy(p => p.Date).TakeLast(90).ToList();
    }

    /// <summary>Uniswap: "0.3%". Slipstream: "CL100 - 0.3%" (tick spacing, then fee).</summary>
    internal static (decimal? FeePercent, int? TickSpacing) ParseMeta(string? meta)
    {
        if (meta is null) return (null, null);
        var m = MetaPattern().Match(meta);
        if (!m.Success) return (null, null);
        var fee = decimal.Parse(m.Groups["fee"].Value, CultureInfo.InvariantCulture);
        int? spacing = m.Groups["spacing"].Success ? int.Parse(m.Groups["spacing"].Value, CultureInfo.InvariantCulture) : null;
        return (fee, spacing);
    }

    [GeneratedRegex(@"^(?:CL(?<spacing>\d+)\s*-\s*)?(?<fee>\d+(?:\.\d+)?)%$")]
    private static partial Regex MetaPattern();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
}
