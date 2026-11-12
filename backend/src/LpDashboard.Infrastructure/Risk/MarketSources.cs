using System.Globalization;
using System.Text.Json;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Options;
using static LpDashboard.Infrastructure.Risk.RiskHttp;

namespace LpDashboard.Infrastructure.Risk;

/// <summary>
/// DexScreener (free, no key): pair age, liquidity and 24 h activity. It has no unique-trader count, so the wash-trading
/// check is a heuristic. Only token and pool addresses are sent.
/// </summary>
internal sealed class DexScreenerSource : IMarketFactsSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public DexScreenerSource(HttpClient http, IOptions<RiskSettings> settings)
    {
        _http = http;
        _pacer = new RequestPacer(settings.Value.DexScreener.RequestsPerMinute);
    }

    public async Task<SourceOutcome<MarketFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
    {
        if (ChainSlug(chain) is not { } slug) return SourceOutcome<MarketFacts>.Fail("chain not supported");
        using var response = await GetJsonAsync(_http, _pacer, $"latest/dex/tokens/{token}", ct);
        return response.Document is { } doc ? ParseToken(doc.RootElement, token, slug) : SourceOutcome<MarketFacts>.Fail(response.Failure ?? "no data");
    }

    public async Task<SourceOutcome<PoolMarketFacts>> GetPoolAsync(Chain chain, string pool, CancellationToken ct)
    {
        if (ChainSlug(chain) is not { } slug) return SourceOutcome<PoolMarketFacts>.Fail("chain not supported");
        using var response = await GetJsonAsync(_http, _pacer, $"latest/dex/pairs/{slug}/{pool}", ct);
        return response.Document is { } doc ? ParsePool(doc.RootElement, slug) : SourceOutcome<PoolMarketFacts>.Fail(response.Failure ?? "no data");
    }

    /// <summary>Pairs of this token on this chain, summed. A token with no pair at all answers with zero liquidity.</summary>
    internal static SourceOutcome<MarketFacts> ParseToken(JsonElement root, string token, string chainSlug)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pairs", out var pairs))
            return SourceOutcome<MarketFacts>.Fail("unreadable response");
        // "pairs": null is how DexScreener says it knows nothing about the token.
        if (pairs.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) return SourceOutcome<MarketFacts>.Fail("unreadable response");

        decimal liquidity = 0, volume = 0;
        var trades = 0;
        DateTime? oldest = null;
        var addresses = new List<string>();

        if (pairs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pair in pairs.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(Str(pair, "chainId"), chainSlug, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsSide(pair, "baseToken", token) && !IsSide(pair, "quoteToken", token)) continue;

                if (Str(pair, "pairAddress") is { Length: > 0 } address) addresses.Add(address.ToLowerInvariant());
                if (pair.TryGetProperty("liquidity", out var l)) liquidity += Dec(l, "usd") ?? 0;
                if (pair.TryGetProperty("volume", out var v)) volume += Dec(v, "h24") ?? 0;
                if (pair.TryGetProperty("txns", out var t) && t.TryGetProperty("h24", out var h24))
                    trades += (Int(h24, "buys") ?? 0) + (Int(h24, "sells") ?? 0);
                if (Dec(pair, "pairCreatedAt") is { } ms && ms > 0)
                {
                    var created = DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
                    if (oldest is null || created < oldest) oldest = created;
                }
            }
        }

        return SourceOutcome<MarketFacts>.Ok(new MarketFacts(liquidity, volume, trades, oldest, addresses));
    }

    internal static SourceOutcome<PoolMarketFacts> ParsePool(JsonElement root, string chainSlug)
    {
        if (root.ValueKind != JsonValueKind.Object) return SourceOutcome<PoolMarketFacts>.Fail("unreadable response");

        JsonElement? pair = null;
        if (root.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
            pair = pairs.EnumerateArray().FirstOrDefault(p => string.Equals(Str(p, "chainId"), chainSlug, StringComparison.OrdinalIgnoreCase));
        else if (root.TryGetProperty("pair", out var single) && single.ValueKind == JsonValueKind.Object)
            pair = single;

        // DexScreener only indexes pools that have traded.
        if (pair is not { ValueKind: JsonValueKind.Object } p) return SourceOutcome<PoolMarketFacts>.Fail("pool not indexed");

        DateTime? created = Dec(p, "pairCreatedAt") is { } ms && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : null;
        decimal? liquidity = p.TryGetProperty("liquidity", out var l) ? Dec(l, "usd") : null;
        return SourceOutcome<PoolMarketFacts>.Ok(new PoolMarketFacts(created, liquidity));
    }

    private static bool IsSide(JsonElement pair, string side, string token) =>
        pair.TryGetProperty(side, out var s) && string.Equals(Str(s, "address"), token, StringComparison.OrdinalIgnoreCase);

    private static string? ChainSlug(Chain chain) => chain switch
    {
        Chain.Base => "base",
        Chain.Ethereum => "ethereum",
        _ => null,
    };
}

/// <summary>
/// Etherscan v2 (one key, many chains): verified source and creation time. Optional: the free tier may not cover Base, in
/// which case the source answers with the API's own message and the check reads as "did not run". The key is in the URL, so
/// URLs are never logged or put in a failure reason.
/// </summary>
internal sealed class EtherscanSource : IContractInfoSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer = new(100);
    private readonly string? _apiKey;

    public EtherscanSource(HttpClient http, IOptions<RiskSettings> settings)
    {
        _http = http;
        _apiKey = settings.Value.Etherscan.ApiKey?.Trim();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<SourceOutcome<ContractFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
    {
        if (!IsConfigured) return SourceOutcome<ContractFacts>.Fail("not configured");
        if (ChainIds.Of(chain) is not { } chainId) return SourceOutcome<ContractFacts>.Fail("chain not supported");
        var key = Uri.EscapeDataString(_apiKey!);

        using var source = await GetJsonAsync(_http, _pacer, $"v2/api?chainid={chainId}&module=contract&action=getsourcecode&address={token}&apikey={key}", ct);
        if (source.Document is not { } sourceDoc) return SourceOutcome<ContractFacts>.Fail(source.Failure ?? "no data");
        if (ParseSource(sourceDoc.RootElement) is not { } verified) return SourceOutcome<ContractFacts>.Fail(Message(sourceDoc.RootElement));

        // The creation time is a second call; if only it fails the verified flag is still worth having.
        DateTime? created = null;
        using var creation = await GetJsonAsync(_http, _pacer, $"v2/api?chainid={chainId}&module=contract&action=getcontractcreation&contractaddresses={token}&apikey={key}", ct);
        if (creation.Document is { } creationDoc) created = ParseCreation(creationDoc.RootElement);

        return SourceOutcome<ContractFacts>.Ok(new ContractFacts(verified, created));
    }

    /// <summary>True when the explorer holds verified source for the address, false when not, null when the reply is an API error.</summary>
    internal static bool? ParseSource(JsonElement root)
    {
        if (Str(root, "status") != "1" || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return null;
        var first = result[0];
        return !string.IsNullOrWhiteSpace(Str(first, "SourceCode"))
            && Str(first, "ABI") is not "Contract source code not verified";
    }

    internal static DateTime? ParseCreation(JsonElement root)
    {
        if (Str(root, "status") != "1" || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return null;
        return long.TryParse(Str(result[0], "timestamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
    }

    private static string Message(JsonElement root)
    {
        // The API's own error text ("Free API access is not supported for this chain"), never the request.
        var text = Str(root, "result") is { Length: > 0 } r && !r.StartsWith('[') ? r : Str(root, "message") ?? "no data";
        return text.Length <= 90 ? text : text[..90];
    }
}
