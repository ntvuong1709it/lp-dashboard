using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;
using Microsoft.Extensions.Options;
using LpDashboard.Infrastructure.Risk;
using static LpDashboard.Infrastructure.Risk.RiskHttp;

namespace LpDashboard.Infrastructure.Market;

/// <summary>Nasdaq's public earnings calendar (no key, unofficial): one call per day. It has market cap and forecast EPS but no actual EPS.</summary>
internal sealed class NasdaqEarningsCalendarSource : IEarningsCalendarSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public NasdaqEarningsCalendarSource(HttpClient http, IOptions<MarketSettings> settings)
    {
        _http = http;
        _pacer = new RequestPacer(settings.Value.RequestsPerMinute);
    }

    public async Task<SourceOutcome<IReadOnlyList<RawEarning>>> GetDayAsync(DateOnly day, CancellationToken ct)
    {
        using var response = await GetJsonAsync(_http, _pacer, $"api/calendar/earnings?date={NasdaqJson.DayParam(day)}", ct);
        return response.Document is { } doc
            ? Parse(doc.RootElement, day)
            : SourceOutcome<IReadOnlyList<RawEarning>>.Fail(response.Failure ?? "no data");
    }

    internal static SourceOutcome<IReadOnlyList<RawEarning>> Parse(JsonElement root, DateOnly day)
    {
        if (NasdaqJson.Rows(root) is not { } rows) return SourceOutcome<IReadOnlyList<RawEarning>>.Fail("unreadable response");

        var result = new List<RawEarning>();
        foreach (var row in rows)
        {
            var symbol = NasdaqJson.Text(row, "symbol");
            if (symbol.Length == 0) continue;
            var timing = NasdaqJson.Text(row, "time") switch
            {
                "time-pre-market" => "pre-market",
                "time-after-hours" => "after-hours",
                _ => "not-supplied",
            };
            result.Add(new RawEarning(
                day,
                timing,
                symbol,
                NasdaqJson.Text(row, "name"),
                NasdaqJson.Optional(row, "marketCap"),
                NasdaqJson.Optional(row, "epsForecast")));
        }
        return SourceOutcome<IReadOnlyList<RawEarning>>.Ok(result);
    }
}
