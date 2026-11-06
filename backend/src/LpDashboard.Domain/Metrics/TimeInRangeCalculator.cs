using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Metrics;

public sealed record TickSample(DateTime Timestamp, int Tick);

/// <param name="Percent">Share of the covered time the position was in range (0-100).</param>
/// <param name="MeasuredHours">Covered by stored snapshots (the real pool tick).</param>
/// <param name="EstimatedHours">Covered only by a tick inferred from historical token prices (history before the first sync, gaps while the app was off).</param>
/// <param name="SpanHours">First to last sample of either kind; covered/span shows how complete the data is.</param>
/// <param name="UncertainHours">Estimated time whose inferred tick sat within the estimate's error of a range edge, so the in/out call could be wrong.</param>
/// <param name="PercentLow">The result if every uncertain stretch were really out of range.</param>
/// <param name="PercentHigh">The result if every uncertain stretch were really in range.</param>
public sealed record TimeInRange(
    double Percent, double MeasuredHours, double EstimatedHours, double SpanHours,
    double UncertainHours, double PercentLow, double PercentHigh)
{
    public double TrackedHours => MeasuredHours + EstimatedHours;

    /// <summary>How much to trust <see cref="Percent"/>: High when the band is under 2 points wide (always the case for measured data), Medium under 10, else Low.</summary>
    public RangeConfidence Confidence => (PercentHigh - PercentLow) switch
    {
        < 2 => RangeConfidence.High,
        < 10 => RangeConfidence.Medium,
        _ => RangeConfidence.Low,
    };
}

public enum RangeConfidence
{
    High,
    Medium,
    Low
}

/// <summary>
/// Time in range from tick samples. Each interval between two consecutive samples counts as the state of the earlier
/// one. Measured intervals (from snapshots) always win; time they do not cover can be filled from an estimated
/// timeline (ticks inferred from market prices). Intervals longer than the allowed gap are skipped rather than guessed,
/// and the result is null when nothing is covered.
/// </summary>
public static class TimeInRangeCalculator
{
    /// <summary>
    /// Ticks an estimated tick can be off by: spot checks against deposit events found the price-implied pool price
    /// 0.6-1.3% (60-130 ticks) away from the real one. An estimate this close to an edge may land on the wrong side.
    /// </summary>
    public const int EstimateToleranceTicks = 130;

    private sealed record Interval(DateTime Start, DateTime End, bool InRange, bool Uncertain = false);

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
        var measuredIntervals = Intervals(measured, tickLower, tickUpper, maxMeasuredGap, toleranceTicks: 0);
        var estimatedIntervals = Intervals(estimated, tickLower, tickUpper, maxEstimatedGap, EstimateToleranceTicks);

        double measuredHours = measuredIntervals.Sum(Hours);
        double inRange = measuredIntervals.Where(i => i.InRange).Sum(Hours);

        double estimatedHours = 0, uncertainIn = 0, uncertainOut = 0;
        foreach (var interval in estimatedIntervals)
        {
            foreach (var piece in Subtract(interval, measuredIntervals))
            {
                estimatedHours += Hours(piece);
                if (piece.InRange) inRange += Hours(piece);
                if (piece.Uncertain)
                {
                    if (piece.InRange) uncertainIn += Hours(piece);
                    else uncertainOut += Hours(piece);
                }
            }
        }

        var covered = measuredHours + estimatedHours;
        if (covered == 0) return null;

        var all = measured.Concat(estimated).Select(s => s.Timestamp).ToList();
        return new TimeInRange(
            inRange / covered * 100, measuredHours, estimatedHours, (all.Max() - all.Min()).TotalHours,
            uncertainIn + uncertainOut,
            (inRange - uncertainIn) / covered * 100,
            (inRange + uncertainOut) / covered * 100);
    }

    private static double Hours(Interval i) => (i.End - i.Start).TotalHours;

    private static List<Interval> Intervals(IReadOnlyList<TickSample> samples, int tickLower, int tickUpper, TimeSpan maxGap, int toleranceTicks)
    {
        bool NearEdge(int tick) =>
            toleranceTicks > 0 && (Math.Abs((long)tick - tickLower) <= toleranceTicks || Math.Abs((long)tick - tickUpper) <= toleranceTicks);

        var ordered = samples.OrderBy(s => s.Timestamp).ToList();
        var result = new List<Interval>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var span = ordered[i + 1].Timestamp - ordered[i].Timestamp;
            if (span <= TimeSpan.Zero || span > maxGap) continue;

            result.Add(new Interval(
                ordered[i].Timestamp, ordered[i + 1].Timestamp,
                PositionMath.IsInRange(ordered[i].Tick, tickLower, tickUpper), NearEdge(ordered[i].Tick)));
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
