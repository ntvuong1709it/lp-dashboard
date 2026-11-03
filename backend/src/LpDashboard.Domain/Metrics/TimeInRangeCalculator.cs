using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Metrics;

public sealed record TickSample(DateTime Timestamp, int Tick);

/// <param name="Percent">Share of the covered time the position was in range (0-100).</param>
/// <param name="MeasuredHours">Covered by stored snapshots (the real pool tick).</param>
/// <param name="EstimatedHours">Covered only by a tick inferred from historical token prices (history before the first sync, gaps while the app was off).</param>
/// <param name="SpanHours">First to last sample of either kind; covered/span shows how complete the data is.</param>
public sealed record TimeInRange(double Percent, double MeasuredHours, double EstimatedHours, double SpanHours)
{
    public double TrackedHours => MeasuredHours + EstimatedHours;
}

/// <summary>
/// Time in range from tick samples. Each interval between two consecutive samples counts as the state of the earlier
/// one. Measured intervals (from snapshots) always win; time they do not cover can be filled from an estimated
/// timeline (ticks inferred from market prices). Intervals longer than the allowed gap are skipped rather than guessed,
/// and the result is null when nothing is covered.
/// </summary>
public static class TimeInRangeCalculator
{
    private sealed record Interval(DateTime Start, DateTime End, bool InRange);

    public static TimeInRange? Calculate(IReadOnlyList<TickSample> measured, int tickLower, int tickUpper, TimeSpan maxGap) =>
        Calculate(measured, [], tickLower, tickUpper, maxGap, maxGap);

    public static TimeInRange? Calculate(
        IReadOnlyList<TickSample> measured,
        IReadOnlyList<TickSample> estimated,
        int tickLower,
        int tickUpper,
        TimeSpan maxMeasuredGap,
        TimeSpan maxEstimatedGap)
    {
        var measuredIntervals = Intervals(measured, tickLower, tickUpper, maxMeasuredGap);
        var estimatedIntervals = Intervals(estimated, tickLower, tickUpper, maxEstimatedGap);

        double measuredHours = measuredIntervals.Sum(Hours);
        double inRange = measuredIntervals.Where(i => i.InRange).Sum(Hours);

        double estimatedHours = 0;
        foreach (var interval in estimatedIntervals)
        {
            foreach (var piece in Subtract(interval, measuredIntervals))
            {
                estimatedHours += Hours(piece);
                if (piece.InRange) inRange += Hours(piece);
            }
        }

        var covered = measuredHours + estimatedHours;
        if (covered == 0) return null;

        var all = measured.Concat(estimated).Select(s => s.Timestamp).ToList();
        return new TimeInRange(inRange / covered * 100, measuredHours, estimatedHours, (all.Max() - all.Min()).TotalHours);
    }

    private static double Hours(Interval i) => (i.End - i.Start).TotalHours;

    private static List<Interval> Intervals(IReadOnlyList<TickSample> samples, int tickLower, int tickUpper, TimeSpan maxGap)
    {
        var ordered = samples.OrderBy(s => s.Timestamp).ToList();
        var result = new List<Interval>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var span = ordered[i + 1].Timestamp - ordered[i].Timestamp;
            if (span <= TimeSpan.Zero || span > maxGap) continue;

            result.Add(new Interval(ordered[i].Timestamp, ordered[i + 1].Timestamp, PositionMath.IsInRange(ordered[i].Tick, tickLower, tickUpper)));
        }
        return result;
    }

    /// <summary>The parts of <paramref name="interval"/> not covered by any of the (sorted, non-overlapping) <paramref name="covered"/> intervals.</summary>
    private static IEnumerable<Interval> Subtract(Interval interval, IReadOnlyList<Interval> covered)
    {
        var cursor = interval.Start;
        foreach (var c in covered)
        {
            if (c.End <= cursor) continue;
            if (c.Start >= interval.End) break;

            if (c.Start > cursor) yield return interval with { Start = cursor, End = c.Start };
            cursor = c.End > cursor ? c.End : cursor;
        }

        if (cursor < interval.End) yield return interval with { Start = cursor };
    }
}
