using System.Globalization;
using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;
using LpDashboard.Domain.Market;
using Microsoft.Extensions.Options;
using static LpDashboard.Infrastructure.Risk.RiskHttp;
using LpDashboard.Infrastructure.Risk;

namespace LpDashboard.Infrastructure.Market;

/// <summary>Shared reading of Nasdaq's calendar JSON: <c>{"data":{"rows":[...]}}</c>, empty values as "&amp;nbsp;" or " ".</summary>
internal static class NasdaqJson
{
    /// <summary>Nasdaq's error code for "No record found": how it answers a day with nothing on the calendar.</summary>
    private const int NoRecordCode = 1002;

    /// <summary>
    /// The rows; an empty list for <c>"rows": null</c> and for the "No record found" answer (<c>"data": null</c> with status
    /// 200 and code 1002, seen for a day without events); null when the body is not a calendar answer.
    /// </summary>
    public static IEnumerable<JsonElement>? Rows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)) return null;
        if (data.ValueKind == JsonValueKind.Null) return IsNoRecord(root) ? [] : null;
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("rows", out var rows) || rows.ValueKind == JsonValueKind.Null) return [];
        return rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : null;
    }

    private static bool IsNoRecord(JsonElement root) =>
        root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object
        && status.TryGetProperty("rCode", out var rCode) && rCode.ValueKind == JsonValueKind.Number && rCode.GetInt32() == 200
        && status.TryGetProperty("bCodeMessage", out var messages) && messages.ValueKind == JsonValueKind.Array
        && messages.EnumerateArray().Any(m => m.ValueKind == JsonValueKind.Object
            && m.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() == NoRecordCode);

    public static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? MarketText.Clean(v.GetString()) : "";

    /// <summary>Blank becomes null: "not published".</summary>
    public static string? Optional(JsonElement row, string name) => Text(row, name) is { Length: > 0 } t ? t : null;

    public static string DayParam(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Nasdaq's public economic calendar (no key, unofficial): one call per day. Only a date is sent. The time column is
/// labelled "gmt" but is US Eastern.
/// </summary>
internal sealed class NasdaqEconomicCalendarSource : IEconomicCalendarSource
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    public NasdaqEconomicCalendarSource(HttpClient http, IOptions<MarketSettings> settings)
    {
        _http = http;
        _pacer = new RequestPacer(settings.Value.RequestsPerMinute);
    }

    public async Task<SourceOutcome<IReadOnlyList<RawEconomicEvent>>> GetDayAsync(DateOnly day, CancellationToken ct)
    {
        using var response = await GetJsonAsync(_http, _pacer, $"api/calendar/economicevents?date={NasdaqJson.DayParam(day)}", ct);
        return response.Document is { } doc
            ? Parse(doc.RootElement, day)
            : SourceOutcome<IReadOnlyList<RawEconomicEvent>>.Fail(response.Failure ?? "no data");
    }

    internal static SourceOutcome<IReadOnlyList<RawEconomicEvent>> Parse(JsonElement root, DateOnly day)
    {
        if (NasdaqJson.Rows(root) is not { } rows) return SourceOutcome<IReadOnlyList<RawEconomicEvent>>.Fail("unreadable response");

        var result = new List<RawEconomicEvent>();
        foreach (var row in rows)
        {
            var name = NasdaqJson.Text(row, "eventName");
            if (name.Length == 0) continue;
            result.Add(new RawEconomicEvent(
                day,
                NasdaqJson.Text(row, "gmt"),
                NasdaqJson.Text(row, "country"),
                name,
                NasdaqJson.Optional(row, "actual"),
                NasdaqJson.Optional(row, "consensus"),
                NasdaqJson.Optional(row, "previous")));
        }
        return SourceOutcome<IReadOnlyList<RawEconomicEvent>>.Ok(result);
    }
}
