using System.Net;
using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Domain.Market;
using LpDashboard.Infrastructure.Market;

namespace LpDashboard.Domain.Tests;

/// <summary>
/// The Coinbase and Binance premium adapter parses recorded answers (Fixtures/market/coinbase_*.json and binance_*.json,
/// real responses saved on 2026-11-18) and inline bodies. No test calls the network.
/// </summary>
public class PremiumSourceTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string FixtureText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "market", name));

    private static Dictionary<long, decimal> Prices(params (long Open, decimal Price)[] rows) =>
        rows.ToDictionary(r => r.Open, r => r.Price);

    // ---- parsing

    [Fact]
    public void Real_fixtures_parse_and_join()
    {
        var btcUsd = CoinbaseBinancePremiumSource.ParseCoinbase(Json(FixtureText("coinbase_btcusd_candles_3600.json")));
        var usdt = CoinbaseBinancePremiumSource.ParseCoinbase(Json(FixtureText("coinbase_usdtusd_candles_3600.json")));
        var btcUsdt = CoinbaseBinancePremiumSource.ParseBinance(Json(FixtureText("binance_btcusdt_klines_1h.json")));

        Assert.NotEmpty(btcUsd);
        Assert.NotEmpty(usdt);
        Assert.NotEmpty(btcUsdt);
        var hours = CoinbaseBinancePremiumSource.Join(btcUsd, btcUsdt, usdt);
        Assert.True(hours.Count >= 100, $"joined {hours.Count} hours");
        for (var i = 1; i < hours.Count; i++) Assert.True(hours[i].At > hours[i - 1].At);
        Assert.All(hours, h => Assert.InRange(h.Percent, -1m, 1m));
    }

    [Fact]
    public void Coinbase_rows_are_read_by_open_time_with_the_close() =>
        Assert.Equal(Prices((1794960000, 4.5m)), CoinbaseBinancePremiumSource.ParseCoinbase(Json("[[1794960000,1,2,3,4.5,9]]")));

    [Fact]
    public void Binance_rows_are_read_with_ms_converted_to_seconds() =>
        Assert.Equal(Prices((1794960000, 4.5m)), CoinbaseBinancePremiumSource.ParseBinance(Json("""[[1794960000000,"1","2","3","4.5","9"]]""")));

    [Fact]
    public void Bad_coinbase_rows_are_skipped()
    {
        const string body = """["x",[1],[1794960000,1,2,3,0,9],[1794960000,1,2,3,-3,9],[1794960000,1,2,3,"4",9],[1794960000,1,2,3,4.5,9]]""";

        Assert.Equal(Prices((1794960000, 4.5m)), CoinbaseBinancePremiumSource.ParseCoinbase(Json(body)));
    }

    [Fact]
    public void Bad_binance_rows_are_skipped()
    {
        const string body = """["x",[1],[1794960000000,"1","2","3","0","9"],[1794960000000,"1","2","3","-3","9"],[1794960000000,"1","2","3",4,"9"],[1794960000000,"1","2","3","abc","9"],[1794960000000,"1","2","3","4.5","9"]]""";

        Assert.Equal(Prices((1794960000, 4.5m)), CoinbaseBinancePremiumSource.ParseBinance(Json(body)));
    }

    [Theory]
    [InlineData("""{"message":"NotFound"}""")]
    [InlineData("\"x\"")]
    [InlineData("[]")]
    public void An_unreadable_body_gives_nothing(string body)
    {
        Assert.Empty(CoinbaseBinancePremiumSource.ParseCoinbase(Json(body)));
        Assert.Empty(CoinbaseBinancePremiumSource.ParseBinance(Json(body)));
    }

    // ---- joining

    [Fact]
    public void Join_keeps_only_common_hours_and_computes_the_gap()
    {
        var hours = CoinbaseBinancePremiumSource.Join(
            Prices((1, 101m), (2, 101m), (3, 101m)),
            Prices((2, 100m), (3, 100m), (4, 100m)),
            Prices((1, 1m), (2, 1m), (3, 1m), (4, 1m)));

        Assert.Equal(
            [new PremiumHour(DateTimeOffset.FromUnixTimeSeconds(2), 1.0m), new PremiumHour(DateTimeOffset.FromUnixTimeSeconds(3), 1.0m)],
            hours);
    }

    // Coinbase is 100 x the USDT price, so the dollar gap is zero and only the USDT range decides.
    [Theory]
    [InlineData(89, 0.89, false)]
    [InlineData(111, 1.11, false)]
    [InlineData(90, 0.90, true)]
    [InlineData(110, 1.10, true)]
    public void A_usdt_price_outside_0_90_to_1_10_drops_the_hour(double coinbase, double usdt, bool kept) =>
        Assert.Equal(kept ? 1 : 0, CoinbaseBinancePremiumSource.Join(Prices((1, (decimal)coinbase)), Prices((1, 100m)), Prices((1, (decimal)usdt))).Count);

    [Theory]
    [InlineData(105.1, false)]
    [InlineData(94.9, false)]
    [InlineData(104.9, true)]
    [InlineData(95.1, true)]
    public void A_gap_beyond_5_percent_drops_the_hour(double coinbase, bool kept) =>
        Assert.Equal(kept ? 1 : 0, CoinbaseBinancePremiumSource.Join(Prices((1, (decimal)coinbase)), Prices((1, 100m)), Prices((1, 1m))).Count);

    // ---- the requests

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        private readonly object _gate = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (_gate) Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(name switch
            {
                "coinbase-exchange" => "https://api.exchange.coinbase.com/",
                "binance-data" => "https://data-api.binance.vision/",
                _ => throw new InvalidOperationException($"unexpected client {name}"),
            }),
        };
    }

    private static readonly DateTimeOffset Until = new(2026, 11, 18, 14, 20, 0, TimeSpan.Zero);

    // 1795003200 is 2026-11-18 12:00 UTC, then 13:00 and 14:00. Coinbase BTC-USD 101, USDT-USD 1.0, Binance 100: a gap of 1.0.
    private static readonly long[] Opens = [1795003200, 1795006800, 1795010400];

    private static string CoinbaseBody(decimal close) => "[" + string.Join(",", Opens.Reverse().Select(t => $"[{t},1,2,3,{close.ToString(System.Globalization.CultureInfo.InvariantCulture)},9]")) + "]";

    private static string BinanceBody => "[" + string.Join(",", Opens.Select(t => $"[{t}000,\"1\",\"2\",\"3\",\"100\",\"9\"]")) + "]";

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private static HttpResponseMessage Good(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        return path.Contains("USDT-USD") ? Reply(HttpStatusCode.OK, CoinbaseBody(1.0m))
            : path.Contains("BTC-USD") ? Reply(HttpStatusCode.OK, CoinbaseBody(101m))
            : Reply(HttpStatusCode.OK, BinanceBody);
    }

    private static CoinbaseBinancePremiumSource Source(StubHandler handler) => new(new StubFactory(handler));

    [Fact]
    public async Task A_good_answer_returns_the_joined_hours_oldest_first()
    {
        var outcome = await Source(new StubHandler(Good)).GetHoursAsync(Until, CancellationToken.None);

        Assert.True(outcome.Answered);
        Assert.Equal(Opens.Select(t => new PremiumHour(DateTimeOffset.FromUnixTimeSeconds(t), 1.0m)), outcome.Value);
    }

    [Fact]
    public async Task The_requests_are_exactly_these()
    {
        var handler = new StubHandler(Good);

        await Source(handler).GetHoursAsync(Until, CancellationToken.None);

        // With H = 2026-11-18 14:00 UTC (until floored to the hour): end = H - 300h x i, start = end - 299h, for i = 0, 1, 2.
        string[] ranges =
        [
            "start=2026-11-06T03:00:00Z&end=2026-11-18T14:00:00Z",
            "start=2026-10-24T15:00:00Z&end=2026-11-06T02:00:00Z",
            "start=2026-10-12T03:00:00Z&end=2026-10-24T14:00:00Z",
        ];
        var expected = ranges.Select(r => $"/products/BTC-USD/candles?granularity=3600&{r}")
            .Concat(ranges.Select(r => $"/products/USDT-USD/candles?granularity=3600&{r}"))
            .Append("/api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720")
            .Order().ToList();
        Assert.Equal(expected, handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Order().ToList());
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Null(r.Headers.Authorization);
            Assert.False(r.Headers.Contains("Cookie"));
            Assert.Null(r.Content);
        });
    }

    public static TheoryData<string, string> FailureCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var feed in new[] { "BTC-USD", "USDT-USD", "klines" })
            foreach (var kind in new[] { "451", "500", "network", "html", "timeout" })
                data.Add(feed, kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task Any_failed_request_fails_the_source_without_throwing(string feed, string kind)
    {
        var handler = new StubHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            var isBroken = feed == "klines" ? path.Contains("klines") : path.Contains($"/{feed}/");
            if (!isBroken) return Good(r);
            return kind switch
            {
                "451" => Reply((HttpStatusCode)451, "unavailable for legal reasons"),
                "500" => Reply(HttpStatusCode.InternalServerError, "oops"),
                "network" => throw new HttpRequestException("no route"),
                "timeout" => throw new TaskCanceledException("timed out"),
                _ => Reply(HttpStatusCode.OK, "<html><body>Just a moment...</body></html>"),
            };
        });

        var outcome = await Source(handler).GetHoursAsync(Until, CancellationToken.None);

        Assert.False(outcome.Answered);
        Assert.NotNull(outcome.Failure);
        Assert.DoesNotContain("http", outcome.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coinbase", outcome.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binance", outcome.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Feeds_that_share_no_hour_fail()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.Contains("klines")
            ? Reply(HttpStatusCode.OK, """[[1794999600000,"1","2","3","100","9"]]""") // 11:00, no other feed has it
            : Good(r));

        var outcome = await Source(handler).GetHoursAsync(Until, CancellationToken.None);

        Assert.False(outcome.Answered);
    }
}
