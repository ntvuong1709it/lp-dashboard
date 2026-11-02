using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LpLedger.Core;

namespace LpLedger.Infrastructure.Prices;

/// <summary>Current and historical USD prices from DefiLlama's free coins API (no key).</summary>
public sealed class LlamaPrices(HttpClient http, string chain = "base") : IPriceSource
{
    private readonly ConcurrentDictionary<string, decimal> _historical = new();

    public Task<IReadOnlyDictionary<string, decimal>> CurrentAsync(IEnumerable<string> addresses) =>
        FetchAsync("prices/current/", addresses, "now");

    public Task<IReadOnlyDictionary<string, decimal>> AtAsync(long timestamp, IEnumerable<string> addresses)
    {
        var hour = timestamp / 3600 * 3600; // hourly buckets keep requests few
        return FetchAsync($"prices/historical/{hour}/", addresses, hour.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<IReadOnlyDictionary<string, decimal>> FetchAsync(string path, IEnumerable<string> addresses, string bucket)
    {
        var result = new Dictionary<string, decimal>();
        var missing = new List<string>();
        foreach (var a in addresses.Select(a => a.ToLowerInvariant()).Distinct())
        {
            if (bucket != "now" && _historical.TryGetValue($"{bucket}:{a}", out var hit)) result[a] = hit;
            else missing.Add(a);
        }
        if (missing.Count > 0)
        {
            var ids = string.Join(',', missing.Select(a => $"{chain}:{a}"));
            var body = await http.GetFromJsonAsync<LlamaResponse>(path + ids)
                ?? throw new InvalidOperationException("Price API returned an empty response.");
            foreach (var a in missing)
            {
                if (!body.Coins.TryGetValue($"{chain}:{a}", out var coin))
                    throw new InvalidOperationException($"No price for {a} ({bucket}).");
                result[a] = coin.Price;
                if (bucket != "now") _historical[$"{bucket}:{a}"] = coin.Price;
            }
        }
        return result;
    }

    private sealed record LlamaResponse([property: JsonPropertyName("coins")] Dictionary<string, LlamaCoin> Coins);

    private sealed record LlamaCoin([property: JsonPropertyName("price")] decimal Price);
}
