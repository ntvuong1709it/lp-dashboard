using System.Globalization;
using System.Text.Json;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;

namespace LpDashboard.Infrastructure.Market;

/// <summary>
/// The crypto Fear and Greed index from alternative.me (no key, unofficial): one call for the last 31 days. Nothing is
/// sent but the GET itself, and a failure never names the URL.
/// </summary>
internal sealed class AlternativeMeFearGreedSource(HttpClient http) : IFearGreedSource
{
    private const string Path = "fng/?limit=31";
    private const long LastUnixSecond = 253_402_300_799; // 9999-12-31, the last second DateTimeOffset holds

    public async Task<SourceOutcome<IReadOnlyList<RawFearGreedDay>>> GetDaysAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(Path, ct);
            if (!response.IsSuccessStatusCode) return Fail($"status {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Parse(doc.RootElement);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timed out");
        }
        catch (HttpRequestException)
        {
            return Fail("unreachable");
        }
        catch (JsonException)
        {
            return Fail("unreadable response");
        }
    }

    /// <summary>The usable rows, oldest first, one per day (the first row of a day wins). Rows with a value outside 0 to 100, a non-number or a bad time are skipped.</summary>
    internal static SourceOutcome<IReadOnlyList<RawFearGreedDay>> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Fail("unreadable response");
        if (root.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            return Fail("source reported an error");

        var days = new List<RawFearGreedDay>();
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            if (!Number(row, "value", out var value) || value is < 0 or > 100) continue;
            if (!Number(row, "timestamp", out var seconds) || seconds is < 0 or > LastUnixSecond) continue;
            days.Add(new RawFearGreedDay(DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime), (int)value));
        }

        var result = days.DistinctBy(d => d.Day).OrderBy(d => d.Day).ToList();
        return result.Count == 0 ? Fail("no days") : SourceOutcome<IReadOnlyList<RawFearGreedDay>>.Ok(result);
    }

    /// <summary>The source writes its numbers as strings.</summary>
    private static bool Number(JsonElement row, string name, out long value)
    {
        value = 0;
        return row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && long.TryParse(v.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static SourceOutcome<IReadOnlyList<RawFearGreedDay>> Fail(string reason) =>
        SourceOutcome<IReadOnlyList<RawFearGreedDay>>.Fail(reason);
}
