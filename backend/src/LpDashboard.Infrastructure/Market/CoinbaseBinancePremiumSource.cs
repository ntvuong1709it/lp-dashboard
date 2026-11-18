using System.Globalization;
using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;
using LpDashboard.Domain.Market;

namespace LpDashboard.Infrastructure.Market;

/// <summary>
/// The hourly Coinbase premium from public candles (no key): Coinbase BTC-USD and USDT-USD, and Binance BTC-USDT. Only
/// product names and a time range are sent, and a failure never names a host. If any one request fails the whole source
/// fails: a shorter series would make the averages look more certain than they are.
/// </summary>
internal sealed class CoinbaseBinancePremiumSource(IHttpClientFactory clients) : IBitcoinPremiumSource
{
    private const string CoinbaseClient = "coinbase-exchange";
    private const string BinanceClient = "binance-data";

    /// <summary>Coinbase returns at most 300 candles a call; three calls cover the 720 hours of 30 days.</summary>
    private const int Chunks = 3;
    private const int CandlesPerChunk = 300;
    private const string BinanceKlines = "api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720";

    private const decimal UsdtMin = 0.90m;
    private const decimal UsdtMax = 1.10m;

    /// <summary>A gap this big between two big exchanges is a bad candle, not US buying.</summary>
    private const decimal MaxGapPercent = 5m;

    private sealed record Answer(JsonElement? Body, string? Failure);

    public async Task<SourceOutcome<IReadOnlyList<PremiumHour>>> GetHoursAsync(DateTimeOffset until, CancellationToken ct)
    {
        var u = until.ToUniversalTime();
        var end0 = new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero);
        var coinbase = clients.CreateClient(CoinbaseClient);
        var binance = clients.CreateClient(BinanceClient);

        string Range(int i)
        {
            var end = end0.AddHours(-CandlesPerChunk * i);
            var start = end.AddHours(-(CandlesPerChunk - 1));
            // Invariant on purpose: the ":" in a format string is the culture's time separator and "yyyy" follows its calendar.
            const string Format = "yyyy-MM-ddTHH:mm:ssZ";
            return $"granularity=3600&start={start.ToString(Format, CultureInfo.InvariantCulture)}&end={end.ToString(Format, CultureInfo.InvariantCulture)}";
        }

        var btcUsd = Enumerable.Range(0, Chunks).Select(i => FetchAsync(coinbase, $"products/BTC-USD/candles?{Range(i)}", ct)).ToList();
        var usdt = Enumerable.Range(0, Chunks).Select(i => FetchAsync(coinbase, $"products/USDT-USD/candles?{Range(i)}", ct)).ToList();
        var klines = FetchAsync(binance, BinanceKlines, ct);

        var answers = await Task.WhenAll([.. btcUsd, .. usdt, klines]);
        if (answers.FirstOrDefault(a => a.Failure is not null) is { } failed) return Fail(failed.Failure!);

        var btcChunks = btcUsd.Select(t => ParseCoinbase(t.Result.Body!.Value)).ToList();
        var usdtChunks = usdt.Select(t => ParseCoinbase(t.Result.Body!.Value)).ToList();
        var binanceRows = ParseBinance(klines.Result.Body!.Value);

        // Every request covers a stretch of the 30 days. One that answers with nothing usable (an empty list, an error body)
        // leaves a hole of days that the other chunks would hide, so it fails the source like a failed request does.
        if (btcChunks.Any(c => c.Count == 0) || usdtChunks.Any(c => c.Count == 0) || binanceRows.Count == 0) return Fail("unreadable response");

        var hours = Join(Merge(btcChunks), binanceRows, Merge(usdtChunks));
        return hours.Count == 0 ? Fail("no hours") : SourceOutcome<IReadOnlyList<PremiumHour>>.Ok(hours);
    }

    private static async Task<Answer> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return new Answer(null, $"status {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return new Answer(doc.RootElement.Clone(), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Answer(null, "timed out");
        }
        catch (HttpRequestException)
        {
            return new Answer(null, "unreachable");
        }
        catch (JsonException)
        {
            return new Answer(null, "unreadable response");
        }
    }

    /// <summary>The chunks overlap by nothing but may repeat a candle; the first one seen wins.</summary>
    private static Dictionary<long, decimal> Merge(IEnumerable<IReadOnlyDictionary<long, decimal>> chunks)
    {
        var all = new Dictionary<long, decimal>();
        foreach (var chunk in chunks)
            foreach (var (open, price) in chunk) all.TryAdd(open, price);
        return all;
    }

    /// <summary>Coinbase candles: <c>[openSeconds, low, high, open, close, volume]</c>, numbers. Open time to close price; bad rows are skipped.</summary>
    internal static IReadOnlyDictionary<long, decimal> ParseCoinbase(JsonElement root)
    {
        var result = new Dictionary<long, decimal>();
        if (root.ValueKind != JsonValueKind.Array) return result;
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5) continue;
            if (row[0].ValueKind != JsonValueKind.Number || !row[0].TryGetInt64(out var open)) continue;
            if (row[4].ValueKind != JsonValueKind.Number || !row[4].TryGetDecimal(out var close) || close <= 0) continue;
            result.TryAdd(open, close);
        }
        return result;
    }

    /// <summary>Binance klines: <c>[openMilliseconds, "open", "high", "low", "close", ...]</c>, prices as strings. Open time in seconds to close price; bad rows are skipped.</summary>
    internal static IReadOnlyDictionary<long, decimal> ParseBinance(JsonElement root)
    {
        var result = new Dictionary<long, decimal>();
        if (root.ValueKind != JsonValueKind.Array) return result;
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5) continue;
            if (row[0].ValueKind != JsonValueKind.Number || !row[0].TryGetInt64(out var openMs)) continue;
            if (row[4].ValueKind != JsonValueKind.String
                || !decimal.TryParse(row[4].GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var close) || close <= 0) continue;
            result.TryAdd(openMs / 1000, close);
        }
        return result;
    }

    /// <summary>
    /// The hours present in all three feeds, oldest first, with the gap in dollars. An hour with a USDT price outside 0.90 to
    /// 1.10 or a gap beyond +/-5% is a glitch and is dropped.
    /// </summary>
    internal static IReadOnlyList<PremiumHour> Join(
        IReadOnlyDictionary<long, decimal> btcUsd, IReadOnlyDictionary<long, decimal> btcUsdt, IReadOnlyDictionary<long, decimal> usdtUsd)
    {
        var hours = new List<PremiumHour>();
        foreach (var open in btcUsd.Keys.OrderBy(k => k))
        {
            if (!btcUsdt.TryGetValue(open, out var binance) || !usdtUsd.TryGetValue(open, out var usdt)) continue;
            if (usdt is < UsdtMin or > UsdtMax) continue;

            var gap = PremiumMath.HourlyPercent(btcUsd[open], binance, usdt);
            if (Math.Abs(gap) > MaxGapPercent) continue;
            hours.Add(new PremiumHour(DateTimeOffset.FromUnixTimeSeconds(open), gap));
        }
        return hours;
    }

    private static SourceOutcome<IReadOnlyList<PremiumHour>> Fail(string reason) => SourceOutcome<IReadOnlyList<PremiumHour>>.Fail(reason);
}
