namespace LpDashboard.Domain.Metrics;

public sealed record ValuePoint(DateTime Timestamp, decimal ValueUsd, decimal UnclaimedUsd);

/// <param name="Points">Snapshots of one position, any order.</param>
/// <param name="ClosedAt">The position contributes nothing from this moment on.</param>
public sealed record PositionSeries(IReadOnlyList<ValuePoint> Points, DateTime? ClosedAt);

public sealed record PortfolioHistoryPoint(DateTime Timestamp, decimal ValueUsd, decimal UnclaimedUsd, int PositionCount);

/// <summary>
/// Sums position snapshots into one portfolio series on an hourly grid. Between snapshots a position keeps its last
/// known value; before its first snapshot it does not count (history starts at the first sync, it is not reconstructed).
/// </summary>
public static class PortfolioHistoryCalculator
{
    public static IReadOnlyList<PortfolioHistoryPoint> Build(
        IReadOnlyList<PositionSeries> series, DateTime? from, DateTime? to, int maxPoints = 400)
    {
        var sorted = series
            .Where(s => s.Points.Count > 0)
            .Select(s => (Points: s.Points.OrderBy(p => p.Timestamp).ToArray(), s.ClosedAt))
            .ToList();
        if (sorted.Count == 0) return [];

        var start = sorted.Min(s => s.Points[0].Timestamp);
        var end = sorted.Max(s => s.Points[^1].Timestamp);
        var first = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, start.Kind);

        var hours = Math.Max(1, (int)Math.Ceiling((end - first).TotalHours));
        var step = TimeSpan.FromHours(Math.Max(1, (int)Math.Ceiling(hours / (double)Math.Max(1, maxPoints))));

        var times = new List<DateTime>();
        for (var t = first; t <= end; t += step) times.Add(t);
        if (times[^1] < end) times.Add(end);

        var result = new List<PortfolioHistoryPoint>();
        foreach (var t in times)
        {
            if ((from is not null && t < from) || (to is not null && t >= to)) continue;

            decimal value = 0, unclaimed = 0;
            var count = 0;
            foreach (var (points, closedAt) in sorted)
            {
                if (closedAt is { } closed && closed <= t) continue;
                var latest = LatestAtOrBefore(points, t);
                if (latest is null) continue;

                value += latest.ValueUsd;
                unclaimed += latest.UnclaimedUsd;
                count++;
            }

            if (count > 0) result.Add(new PortfolioHistoryPoint(t, value, unclaimed, count));
        }
        return result;
    }

    private static ValuePoint? LatestAtOrBefore(ValuePoint[] points, DateTime t)
    {
        int lo = 0, hi = points.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].Timestamp <= t)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found < 0 ? null : points[found];
    }
}
