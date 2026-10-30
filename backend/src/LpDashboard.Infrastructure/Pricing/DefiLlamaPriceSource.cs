using System.Text.Json;
using LpDashboard.Application.Abstractions;

namespace LpDashboard.Infrastructure.Pricing;

/// <summary>DefiLlama coins API (free, no key). Keys look like "base:0x..." or "coingecko:ethereum".</summary>
internal sealed class DefiLlamaPriceSource(HttpClient http) : IPriceSource
{
    private const int KeysPerRequest = 40;

    public async Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in keys.Chunk(KeysPerRequest))
        {
            await FetchAsync($"prices/current/{Join(chunk)}", result, ct);
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in keys.Chunk(KeysPerRequest))
        {
            await FetchAsync($"prices/historical/{timestamp}/{Join(chunk)}?searchWidth=6h", result, ct);
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
        IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<PricePoint>>(StringComparer.OrdinalIgnoreCase);
        var stepHours = Math.Max(1, stepSeconds / 3600);
        var period = $"{stepHours}h";
        var searchWidth = $"{Math.Max(6, stepHours)}h";
        foreach (var chunk in keys.Chunk(KeysPerRequest))
        {
            using var response = await http.GetAsync($"chart/{Join(chunk)}?start={start}&span={count}&period={period}&searchWidth={searchWidth}", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!document.RootElement.TryGetProperty("coins", out var coins)) continue;
            foreach (var coin in coins.EnumerateObject())
            {
                if (!coin.Value.TryGetProperty("prices", out var prices) || prices.ValueKind != JsonValueKind.Array) continue;
                var points = new List<PricePoint>();
                foreach (var p in prices.EnumerateArray())
                {
                    if (p.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number
                        && p.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Number)
                    {
                        points.Add(new PricePoint(ts.GetInt64(), price.GetDecimal()));
                    }
                }
                result[coin.Name.ToLowerInvariant()] = points;
            }
        }
        return result;
    }

    private async Task FetchAsync(string path, Dictionary<string, decimal> into, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("coins", out var coins)) return;
        foreach (var coin in coins.EnumerateObject())
        {
            if (coin.Value.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Number)
            {
                into[coin.Name.ToLowerInvariant()] = price.GetDecimal();
            }
        }
    }

    // Keys are generated internally ("chain:0xhex" / "coingecko:id"), so no escaping is needed.
    private static string Join(IEnumerable<string> keys) => string.Join(',', keys.Select(k => k.ToLowerInvariant()));
}
