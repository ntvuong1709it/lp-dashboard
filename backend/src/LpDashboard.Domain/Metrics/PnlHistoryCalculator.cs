namespace LpDashboard.Domain.Metrics;

/// <param name="Timestamp">When the cash flow happened.</param>
/// <param name="Usd">Signed: withdrawals are positive, deposits and gas negative.</param>
public sealed record PnlFlow(DateTime Timestamp, decimal Usd);

/// <param name="ValuePoints">Position value including fees (claimed and unclaimed) over time, any order.</param>
/// <param name="Flows">Deposits, withdrawals and gas.</param>
/// <param name="FinalAt">From this moment the position's P/L is the final figure (close time, or now while open).</param>
/// <param name="FinalPnlUsd">The position's P/L as the metrics report it.</param>
public sealed record PnlSeries(
    IReadOnlyList<ValuePoint> ValuePoints, IReadOnlyList<PnlFlow> Flows, DateTime FinalAt, decimal FinalPnlUsd);

public sealed record PnlPoint(DateTime Timestamp, decimal CumulativePnlUsd);

public sealed record MonthlyPnl(string Month, decimal NetPnlUsd);

public sealed record PnlHistory(IReadOnlyList<PnlPoint> Points, IReadOnlyList<MonthlyPnl> Months);

/// <summary>
/// Cumulative P/L over time on a daily grid: per position, value including fees plus net cash flows so far
/// (withdrawals minus deposits and gas). A position counts from its first value point and settles at its final P/L,
/// so the last point equals the sum of the position P/L figures. Monthly P/L is the change over each calendar month.
/// </summary>
public static class PnlHistoryCalculator
{
    public static PnlHistory Build(IReadOnlyList<PnlSeries> series, DateTime now)
    {
        var usable = series
            .Where(s => s.ValuePoints.Count > 0)
            .Select(s => (
                Points: s.ValuePoints.OrderBy(p => p.Timestamp).ToArray(),
                Flows: s.Flows.OrderBy(f => f.Timestamp).ToArray(),
                s.FinalAt,
                s.FinalPnlUsd))
            .ToList();
        if (usable.Count == 0) return new([], []);

        var firstDay = usable.Min(s => s.Points[0].Timestamp).Date;
        var lastDay = now.Date;
        var points = new List<PnlPoint>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            // Sample at the end of each day, except today which ends now.
            var t = day == lastDay ? now : day.AddDays(1).AddTicks(-1);
            decimal total = 0;
            foreach (var s in usable)
            {
                if (s.Points[0].Timestamp > t) continue;
                if (t >= s.FinalAt)
                {
                    total += s.FinalPnlUsd;
                    continue;
                }

                var latest = s.Points.Last(p => p.Timestamp <= t);
                total += latest.ValueUsd + s.Flows.Where(f => f.Timestamp <= t).Sum(f => f.Usd);
            }
            points.Add(new PnlPoint(t, total));
        }

        var months = new List<MonthlyPnl>();
        decimal previous = 0;
        foreach (var g in points.GroupBy(p => (p.Timestamp.Year, p.Timestamp.Month)))
        {
            var end = g.Last().CumulativePnlUsd;
            months.Add(new MonthlyPnl($"{g.Key.Year:D4}-{g.Key.Month:D2}", end - previous));
            previous = end;
        }

        return new(points, months);
    }
}
