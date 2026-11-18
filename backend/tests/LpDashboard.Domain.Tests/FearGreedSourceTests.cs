using System.Net;
using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Infrastructure.Market;

namespace LpDashboard.Domain.Tests;

/// <summary>
/// The alternative.me adapter parses a recorded answer (Fixtures/market/alternative_fng_limit31.json, a real response
/// saved on 2026-11-18) and inline bodies. No test calls the network.
/// </summary>
public class FearGreedSourceTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string FixtureText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "market", name));

    private static DateOnly D(string s) => DateOnly.Parse(s);

    // 1794960000 is 2026-11-18 00:00 UTC, 1794873600 is the day before.
    private const string TwoDays =
        """{"data":[{"value":"11","value_classification":"Extreme Fear","timestamp":"1794960000"},{"value":"12","value_classification":"Extreme Fear","timestamp":"1794873600"}]}""";

    // ---- parsing

    [Fact]
    public void Real_response_is_read()
    {
        var outcome = AlternativeMeFearGreedSource.Parse(Json(FixtureText("alternative_fng_limit31.json")));

        Assert.True(outcome.Answered);
        var days = outcome.Value!;
        Assert.Equal(31, days.Count);
        for (var i = 1; i < days.Count; i++) Assert.True(days[i].Day > days[i - 1].Day);
        Assert.All(days, d => Assert.InRange(d.Value, 0, 100));
    }

    [Fact]
    public void Rows_are_read_and_sorted_oldest_first()
    {
        var days = AlternativeMeFearGreedSource.Parse(Json(TwoDays)).Value!;

        Assert.Equal([new RawFearGreedDay(D("2026-11-17"), 12), new RawFearGreedDay(D("2026-11-18"), 11)], days);
    }

    [Fact]
    public void Bad_rows_are_skipped_and_good_ones_kept()
    {
        const string body = """
            {"data":[
              {"value":"101","timestamp":"1794960000"},
              {"value":"-1","timestamp":"1794960000"},
              {"value":"abc","timestamp":"1794960000"},
              {"value":"5"},
              {"value":"5","timestamp":"x"},
              {"value":"12","timestamp":"1794873600"}
            ]}
            """;

        var days = AlternativeMeFearGreedSource.Parse(Json(body)).Value!;

        Assert.Equal([new RawFearGreedDay(D("2026-11-17"), 12)], days);
    }

    [Fact]
    public void Two_rows_for_one_day_keep_the_first()
    {
        const string body = """{"data":[{"value":"11","timestamp":"1794960000"},{"value":"99","timestamp":"1794963600"}]}""";

        var days = AlternativeMeFearGreedSource.Parse(Json(body)).Value!;

        Assert.Equal([new RawFearGreedDay(D("2026-11-18"), 11)], days);
    }

    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":"x"}""")]
    [InlineData("""{"foo":1}""")]
    [InlineData("""{"data":[{"value":"5","timestamp":"1794960000"}],"metadata":{"error":"rate limited"}}""")]
    [InlineData("""{"data":[{"value":"500","timestamp":"1794960000"},{"value":"x","timestamp":"1"}]}""")]
    public void Unusable_bodies_fail(string body)
    {
        var outcome = AlternativeMeFearGreedSource.Parse(Json(body));

        Assert.False(outcome.Answered);
        Assert.NotNull(outcome.Failure);
    }

    // ---- the request

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static AlternativeMeFearGreedSource Source(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.alternative.me/") });

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task A_good_answer_is_returned()
    {
        var outcome = await Source(new StubHandler(_ => Reply(HttpStatusCode.OK, TwoDays))).GetDaysAsync(CancellationToken.None);

        Assert.True(outcome.Answered);
        Assert.Equal(2, outcome.Value!.Count);
    }

    [Fact]
    public async Task A_500_answer_fails_without_throwing()
    {
        var outcome = await Source(new StubHandler(_ => Reply(HttpStatusCode.InternalServerError, "oops"))).GetDaysAsync(CancellationToken.None);

        Assert.False(outcome.Answered);
    }

    [Fact]
    public async Task A_network_error_fails_without_throwing()
    {
        var outcome = await Source(new StubHandler(_ => throw new HttpRequestException("no route"))).GetDaysAsync(CancellationToken.None);

        Assert.False(outcome.Answered);
        Assert.DoesNotContain("alternative.me", outcome.Failure);
    }

    [Fact]
    public async Task An_html_body_with_status_200_fails_without_throwing()
    {
        var outcome = await Source(new StubHandler(_ => Reply(HttpStatusCode.OK, "<html><body>Just a moment...</body></html>")))
            .GetDaysAsync(CancellationToken.None);

        Assert.False(outcome.Answered);
    }

    [Fact]
    public async Task The_request_asks_for_31_days_and_sends_nothing_else()
    {
        var handler = new StubHandler(_ => Reply(HttpStatusCode.OK, TwoDays));

        await Source(handler).GetDaysAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.alternative.me/fng/?limit=31", request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
    }
}
