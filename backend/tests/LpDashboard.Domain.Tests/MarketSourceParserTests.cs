using System.Text.Json;
using LpDashboard.Infrastructure.Market;

namespace LpDashboard.Domain.Tests;

/// <summary>
/// The Nasdaq and Fed adapters parse recorded responses (Fixtures/market). The nasdaq_events_20261118, nasdaq_earnings_20261119
/// and fed_press_monetary files are trimmed real answers from 2026-11-17; nasdaq_events_synthetic is hand-made. No test calls the network.
/// </summary>
public class MarketSourceParserTests
{
    private static string FixtureText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "market", name));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static readonly DateOnly Day = new(2026, 11, 18);

    // ---- economic events

    [Fact]
    public void Events_real_response_is_read()
    {
        var outcome = NasdaqEconomicCalendarSource.Parse(Json(FixtureText("nasdaq_events_20261118.json")), Day);

        Assert.True(outcome.Answered);
        var rows = outcome.Value!;
        var core = rows.Single(r => r.Name == "Core Retail Sales");
        Assert.Equal("United States", core.Country);
        Assert.Equal("08:30", core.TimeEastern);
        Assert.Equal(Day, core.Day);
        Assert.Equal("0.4%", core.Consensus);
        Assert.Null(core.Actual); // "&nbsp;" means not published yet
        Assert.Equal(2, rows.Count(r => r.Name == "Retail Sales")); // dedupe is the service's job
        Assert.Null(rows.First(r => r.Name == "Fed Waller Speaks").Consensus); // " " is blank
    }

    [Fact]
    public void Events_keep_published_actuals_and_decode_text()
    {
        var rows = NasdaqEconomicCalendarSource.Parse(Json(FixtureText("nasdaq_events_synthetic.json")), Day).Value!;

        var cpi = rows.Single(r => r.Name == "Core CPI");
        Assert.Equal("3.1%", cpi.Actual);
        Assert.Equal("3.0%", cpi.Consensus);
        Assert.Contains(rows, r => r.Name == "Consumer Confidence & Sentiment");
        Assert.Null(rows.Single(r => r.Name == "CPI").Actual);
        Assert.Equal("All Day", rows.Single(r => r.Name == "Bank Holiday").TimeEastern);
    }

    [Fact]
    public void Events_body_without_data_is_unreadable()
    {
        var outcome = NasdaqEconomicCalendarSource.Parse(Json("""{"foo":1}"""), Day);
        Assert.False(outcome.Answered);
        Assert.Equal("unreadable response", outcome.Failure);
        Assert.False(NasdaqEconomicCalendarSource.Parse(Json("""{"data":null}"""), Day).Answered);
    }

    // Nasdaq's real answer for a day with no events (seen live for Monday 2026-11-16).
    private const string NoRecordFound =
        """{"data":null,"message":null,"status":{"rCode":200,"bCodeMessage":[{"code":1002,"errorMessage":"Economic Events Calendar: No record found."}],"developerMessage":null}}""";

    [Fact]
    public void A_day_with_no_record_is_an_empty_answer_not_a_failure()
    {
        var events = NasdaqEconomicCalendarSource.Parse(Json(NoRecordFound), Day);
        Assert.True(events.Answered);
        Assert.Empty(events.Value!);

        var earnings = NasdaqEarningsCalendarSource.Parse(Json(NoRecordFound.Replace("Economic Events", "Earnings")), Day);
        Assert.True(earnings.Answered);
        Assert.Empty(earnings.Value!);
    }

    [Fact]
    public void Null_data_with_an_error_status_is_still_a_failure() =>
        Assert.False(NasdaqEconomicCalendarSource.Parse(Json("""{"data":null,"status":{"rCode":400,"bCodeMessage":[{"code":1001,"errorMessage":"bad"}]}}"""), Day).Answered);

    [Fact]
    public void Events_null_rows_is_an_empty_answer()
    {
        var outcome = NasdaqEconomicCalendarSource.Parse(Json("""{"data":{"rows":null}}"""), Day);
        Assert.True(outcome.Answered);
        Assert.Empty(outcome.Value!);
    }

    // ---- earnings

    [Fact]
    public void Earnings_real_response_is_read()
    {
        var outcome = NasdaqEarningsCalendarSource.Parse(Json(FixtureText("nasdaq_earnings_20261119.json")), new DateOnly(2026, 11, 19));

        Assert.True(outcome.Answered);
        var wmt = outcome.Value!.Single(r => r.Symbol == "WMT");
        Assert.Equal("Walmart Inc.", wmt.Company);
        Assert.Equal("pre-market", wmt.Timing);
        Assert.Equal("$1,016,233,530,000", wmt.MarketCap);
        Assert.Equal("$0.64", wmt.EpsForecast);
        Assert.Equal("after-hours", outcome.Value!.Single(r => r.Symbol == "INTU").Timing);
        Assert.Equal("not-supplied", outcome.Value!.Single(r => r.Symbol == "CPRT").Timing);
    }

    [Fact]
    public void Earnings_body_without_data_is_unreadable() =>
        Assert.False(NasdaqEarningsCalendarSource.Parse(Json("""{"x":1}"""), Day).Answered);

    // ---- headlines

    [Fact]
    public void Fed_feed_is_read_newest_first()
    {
        var outcome = FeedHeadlineSource.Parse(FixtureText("fed_press_monetary.xml"), "Federal Reserve");

        Assert.True(outcome.Answered);
        var items = outcome.Value!;
        Assert.NotEmpty(items);
        Assert.Equal(items.OrderByDescending(i => i.PublishedAt).Select(i => i.Url), items.Select(i => i.Url));
        var schedule = items.Single(i => i.Title.StartsWith("Federal Reserve announces revised tentative schedule"));
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 17, 45, 0, TimeSpan.Zero), schedule.PublishedAt);
        Assert.EndsWith("monetary20261112a.htm", schedule.Url);
        Assert.Equal("Federal Reserve", schedule.Source);
    }

    [Fact]
    public void Feed_images_are_read_from_media_and_enclosure_tags_and_only_over_https()
    {
        const string xml = """
            <rss xmlns:media="http://search.yahoo.com/mrss/"><channel>
              <item><title>A</title><link>https://x/a</link><pubDate>Thu, 12 Nov 2026 10:00:00 GMT</pubDate>
                <media:thumbnail url="https://img.example/a.jpg"/></item>
              <item><title>B</title><link>https://x/b</link><pubDate>Thu, 12 Nov 2026 09:00:00 GMT</pubDate>
                <media:content url="https://img.example/b.jpg" medium="image"/></item>
              <item><title>C</title><link>https://x/c</link><pubDate>Thu, 12 Nov 2026 08:00:00 GMT</pubDate>
                <enclosure url="https://img.example/c.png" type="image/png" length="1"/></item>
              <item><title>D</title><link>https://x/d</link><pubDate>Thu, 12 Nov 2026 07:00:00 GMT</pubDate>
                <media:thumbnail url="http://img.example/d.jpg"/></item>
              <item><title>E</title><link>https://x/e</link><pubDate>Thu, 12 Nov 2026 06:00:00 GMT</pubDate>
                <enclosure url="https://img.example/e.mp3" type="audio/mpeg" length="1"/></item>
            </channel></rss>
            """;

        var items = FeedHeadlineSource.Parse(xml, "X").Value!;

        Assert.Equal("https://img.example/a.jpg", items.Single(i => i.Title == "A").ImageUrl);
        Assert.Equal("https://img.example/b.jpg", items.Single(i => i.Title == "B").ImageUrl);
        Assert.Equal("https://img.example/c.png", items.Single(i => i.Title == "C").ImageUrl);
        Assert.Null(items.Single(i => i.Title == "D").ImageUrl);
        Assert.Null(items.Single(i => i.Title == "E").ImageUrl);
    }

    [Fact]
    public void Malformed_feed_fails() =>
        Assert.False(FeedHeadlineSource.Parse("<rss><channel><item>", "X").Answered);
}
