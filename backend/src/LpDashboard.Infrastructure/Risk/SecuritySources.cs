using System.Text.Json;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Options;
using static LpDashboard.Infrastructure.Risk.RiskHttp;

namespace LpDashboard.Infrastructure.Risk;

/// <summary>
/// GoPlus Security API (free, no key): <c>GET /api/v1/token_security/{chainId}?contract_addresses=…</c>. Only the token
/// address is sent. The response carries no rate-limit headers, so the limiter is a configured requests-per-minute.
/// </summary>
internal sealed class GoPlusSource : IGoPlusSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public GoPlusSource(HttpClient http, IOptions<RiskSettings> settings)
    {
        _http = http;
        _pacer = new RequestPacer(settings.Value.GoPlus.RequestsPerMinute);
    }

    public async Task<SourceOutcome<GoPlusFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
    {
        if (ChainIds.Of(chain) is not { } chainId) return SourceOutcome<GoPlusFacts>.Fail("chain not supported");
        using var response = await GetJsonAsync(_http, _pacer, $"api/v1/token_security/{chainId}?contract_addresses={token}", ct);
        return response.Document is { } doc ? Parse(doc.RootElement, token) : SourceOutcome<GoPlusFacts>.Fail(response.Failure ?? "no data");
    }

    internal static SourceOutcome<GoPlusFacts> Parse(JsonElement root, string token)
    {
        if (root.ValueKind != JsonValueKind.Object) return SourceOutcome<GoPlusFacts>.Fail("unreadable response");

        // GoPlus answers HTTP 200 with a non-1 code for its own errors (4029 is "too many requests"). Codes 2 and 3 come with
        // a result that is usable but incomplete (seen for Uniswap v4 tokens): the facts are kept, the check is "partial".
        var code = Int(root, "code");
        if (code is { } c0 && c0 != 1 && c0 is not (2 or 3))
            return SourceOutcome<GoPlusFacts>.Fail(c0 == 4029 ? "rate limited" : $"GoPlus code {c0}");

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            return SourceOutcome<GoPlusFacts>.Fail("no data");

        JsonElement? entry = null;
        foreach (var property in result.EnumerateObject())
        {
            if (string.Equals(property.Name, token, StringComparison.OrdinalIgnoreCase)) entry = property.Value;
        }

        // An unknown token comes back as an empty object: no data, not "no flags".
        if (entry is not { ValueKind: JsonValueKind.Object } e || !e.EnumerateObject().Any())
            return SourceOutcome<GoPlusFacts>.Fail("token not known to GoPlus");

        var pools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (e.TryGetProperty("dex", out var dex) && dex.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in dex.EnumerateArray())
            {
                if (Str(d, "pair") is { Length: > 0 } pair) pools.Add(pair);
                // Uniswap v4 keeps every pool's tokens in one PoolManager: it is the "holder", not a person.
                if (Str(d, "pool_manager") is { Length: > 0 } manager) pools.Add(manager);
            }
        }

        List<HolderFact>? holders = null;
        if (e.TryGetProperty("holders", out var holderArray) && holderArray.ValueKind == JsonValueKind.Array)
        {
            holders = [];
            foreach (var h in holderArray.EnumerateArray())
            {
                if (Str(h, "address") is not { Length: > 0 } address || Dec(h, "percent") is not { } fraction) continue;
                var tag = Str(h, "tag");
                holders.Add(new HolderFact(
                    address.ToLowerInvariant(), fraction * 100m, Flag(h, "is_contract") == true, Flag(h, "is_locked") == true,
                    pools.Contains(address) || LooksLikePool(tag), string.IsNullOrWhiteSpace(tag) ? null : tag));
            }
        }

        var facts = new GoPlusFacts(
            Flag(e, "is_honeypot"),
            Dec(e, "buy_tax") * 100m,
            Dec(e, "sell_tax") * 100m,
            Flag(e, "is_mintable"),
            Flag(e, "is_blacklisted"),
            Flag(e, "transfer_pausable"),
            Str(e, "owner_address") is { Length: > 0 } owner ? owner.ToLowerInvariant() : null,
            Str(e, "creator_address") is { Length: > 0 } creator ? creator.ToLowerInvariant() : null,
            Dec(e, "creator_percent") * 100m,
            Flag(e, "is_open_source"),
            Flag(e, "is_proxy"),
            Int(e, "holder_count"),
            holders,
            Str(e, "token_symbol"));

        // Without the honeypot flag the answer cannot back a green verdict, whatever the code says.
        return code is 1 && facts.IsHoneypot is not null
            ? SourceOutcome<GoPlusFacts>.Ok(facts)
            : SourceOutcome<GoPlusFacts>.Partial(facts, "partial data" + (code is { } n && n != 1 ? $" (code {n})" : ""));
    }

    private static bool LooksLikePool(string? tag) =>
        tag is { Length: > 0 } && (tag.Contains("pool", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("pair", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("router", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("swap", StringComparison.OrdinalIgnoreCase));
}

/// <summary>honeypot.is v2 (free, no key): a simulated buy and sell of the token. Only the token address is sent.</summary>
internal sealed class HoneypotIsSource : IHoneypotSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public HoneypotIsSource(HttpClient http, IOptions<RiskSettings> settings)
    {
        _http = http;
        _pacer = new RequestPacer(settings.Value.HoneypotIs.RequestsPerMinute);
    }

    public async Task<SourceOutcome<HoneypotIsFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
    {
        if (ChainIds.Of(chain) is not { } chainId) return SourceOutcome<HoneypotIsFacts>.Fail("chain not supported");
        using var response = await GetJsonAsync(_http, _pacer, $"v2/IsHoneypot?address={token}&chainID={chainId}", ct);
        if (response.Document is { } doc) return Parse(doc.RootElement);
        // honeypot.is answers 404 when the token has no pair it can trade against.
        return SourceOutcome<HoneypotIsFacts>.Fail(response.Failure == "HTTP 404" ? "no trading pair to simulate" : response.Failure ?? "no data");
    }

    internal static SourceOutcome<HoneypotIsFacts> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return SourceOutcome<HoneypotIsFacts>.Fail("unreadable response");

        // simulationSuccess=false means there was nothing to simulate (no pair, no liquidity): no verdict either way.
        if (Str(root, "simulationSuccess") == "false")
            return SourceOutcome<HoneypotIsFacts>.Fail(Str(root, "simulationError") is { Length: > 0 } why ? $"no simulation: {Trim(why)}" : "no simulation possible");

        if (!root.TryGetProperty("honeypotResult", out var result) || result.ValueKind != JsonValueKind.Object)
            return SourceOutcome<HoneypotIsFacts>.Fail("no data");

        var simulation = root.TryGetProperty("simulationResult", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var isHoneypot = Str(result, "isHoneypot") switch { "true" => true, "false" => false, _ => (bool?)null };
        if (isHoneypot is null) return SourceOutcome<HoneypotIsFacts>.Fail("no verdict");

        return SourceOutcome<HoneypotIsFacts>.Ok(new HoneypotIsFacts(
            isHoneypot, null, Dec(simulation, "buyTax"), Dec(simulation, "sellTax"),
            Str(result, "honeypotReason") is { Length: > 0 } reason ? Trim(reason) : null));
    }

    private static string Trim(string text) => text.Length <= 80 ? text : text[..80];
}

internal static class ChainIds
{
    public static int? Of(Chain chain) => chain switch
    {
        Chain.Base => 8453,
        Chain.Ethereum => 1,
        _ => null,
    };
}
