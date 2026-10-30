using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Metrics;

/// <summary>Unclaimed balances (fees + pending rewards) at one moment.</summary>
public sealed record EarningsPoint(
    DateTime Timestamp,
    decimal Uncollected0,
    decimal Uncollected1,
    decimal PendingReward,
    decimal? Price0Usd,
    decimal? Price1Usd,
    decimal? RewardPriceUsd);

public sealed record DailyEarnings(DateOnly Date, decimal FeesUsd, decimal RewardsUsd, bool HasMissingPrices);

/// <summary>
/// Earnings accrue before they are claimed, so each point's cumulative earned amount is
/// (claimed so far + still unclaimed). The increase between two points is what accrued in that interval.
/// Token amounts are diffed first and priced afterwards, so price moves are not counted as earnings.
/// Collect events must already be split into principal/fees (<see cref="CollectSplitter"/>).
/// Before the first point there is no balance history, so each claim is spread evenly over the time since the previous claim of its kind.
/// </summary>
public static class EarningsCalculator
{
    private readonly record struct Cumulative(decimal Fee0, decimal Fee1, decimal Reward);

    public static IReadOnlyList<DailyEarnings> Calculate(IEnumerable<PositionEvent> events, IEnumerable<EarningsPoint> points, TimeZoneInfo zone)
    {
        var claims = events
            .Where(e => e.Type is PositionEventType.Collect or PositionEventType.RewardClaim)
            .OrderBy(e => e.Timestamp)
            .ToList();
        var ordered = points.OrderBy(p => p.Timestamp).ToList();

        var days = new Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)>();
        BackfillFromClaims(days, events, claims, ordered.FirstOrDefault(), zone);

        EarningsPoint? previousPoint = null;
        Cumulative previous = default;
        decimal claimed0 = 0, claimed1 = 0, claimedReward = 0;
        var next = 0;

        foreach (var point in ordered)
        {
            while (next < claims.Count && claims[next].Timestamp <= point.Timestamp)
            {
                claimed0 += claims[next].Fee0;
                claimed1 += claims[next].Fee1;
                claimedReward += claims[next].RewardAmount;
                next++;
            }

            var current = new Cumulative(claimed0 + point.Uncollected0, claimed1 + point.Uncollected1, claimedReward + point.PendingReward);
            if (previousPoint is not null)
            {
                // Negative deltas come from measurement noise (e.g. a claim landing between two samples); never subtract earnings.
                var d0 = System.Math.Max(0, current.Fee0 - previous.Fee0);
                var d1 = System.Math.Max(0, current.Fee1 - previous.Fee1);
                var dr = System.Math.Max(0, current.Reward - previous.Reward);

                var p0 = point.Price0Usd ?? previousPoint.Price0Usd;
                var p1 = point.Price1Usd ?? previousPoint.Price1Usd;
                var pr = point.RewardPriceUsd ?? previousPoint.RewardPriceUsd;
                var missing = (d0 > 0 && p0 is null) || (d1 > 0 && p1 is null) || (dr > 0 && pr is null);

                var fees = d0 * (p0 ?? 0) + d1 * (p1 ?? 0);
                var rewards = dr * (pr ?? 0);
                if (fees > 0 || rewards > 0 || missing)
                {
                    Spread(days, previousPoint.Timestamp, point.Timestamp, fees, rewards, missing, zone);
                }
            }

            previousPoint = point;
            previous = current;
        }

        return days
            .OrderBy(d => d.Key)
            .Select(d => new DailyEarnings(d.Key, d.Value.Fees, d.Value.Rewards, d.Value.Missing))
            .ToList();
    }

    private static void BackfillFromClaims(
        Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)> days,
        IEnumerable<PositionEvent> events, List<PositionEvent> claims, EarningsPoint? first, TimeZoneInfo zone)
    {
        var cutoff = first?.Timestamp ?? DateTime.MaxValue;
        var opened = events.Select(e => (DateTime?)e.Timestamp).Min() ?? first?.Timestamp;
        if (opened is null) return;

        var lastCollect = opened.Value;
        var lastReward = opened.Value;
        foreach (var e in claims.Where(c => c.Timestamp <= cutoff))
        {
            if (e.Type == PositionEventType.Collect)
            {
                var missing = (e.Fee0 > 0 && e.Price0Usd is null) || (e.Fee1 > 0 && e.Price1Usd is null);
                Spread(days, lastCollect, e.Timestamp, e.Fee0 * (e.Price0Usd ?? 0) + e.Fee1 * (e.Price1Usd ?? 0), 0, missing, zone);
                lastCollect = e.Timestamp;
            }
            else
            {
                var missing = e.RewardAmount > 0 && e.RewardPriceUsd is null;
                Spread(days, lastReward, e.Timestamp, 0, e.RewardAmount * (e.RewardPriceUsd ?? 0), missing, zone);
                lastReward = e.Timestamp;
            }
        }

        // What was already unclaimed when tracking began accrued since the last claim (unknown start: skip).
        if (first is null) return;
        if (lastCollect < first.Timestamp)
        {
            var missing = (first.Uncollected0 > 0 && first.Price0Usd is null) || (first.Uncollected1 > 0 && first.Price1Usd is null);
            Spread(days, lastCollect, first.Timestamp, first.Uncollected0 * (first.Price0Usd ?? 0) + first.Uncollected1 * (first.Price1Usd ?? 0), 0, missing, zone);
        }
        if (lastReward < first.Timestamp)
        {
            var missing = first.PendingReward > 0 && first.RewardPriceUsd is null;
            Spread(days, lastReward, first.Timestamp, 0, first.PendingReward * (first.RewardPriceUsd ?? 0), missing, zone);
        }
    }

    /// <summary>Distributes an interval's earnings over the local days it covers, proportionally to time.</summary>
    private static void Spread(
        Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)> days,
        DateTime start, DateTime end, decimal fees, decimal rewards, bool missing, TimeZoneInfo zone)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        end = DateTime.SpecifyKind(end, DateTimeKind.Utc);
        var totalSeconds = (end - start).TotalSeconds;
        var first = LocalDate(start, zone);
        var last = LocalDate(end, zone);

        if (totalSeconds <= 0 || first == last)
        {
            Add(days, last, fees, rewards, missing);
            return;
        }

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var from = start > DayStartUtc(day, zone) ? start : DayStartUtc(day, zone);
            var dayEnd = DayStartUtc(day.AddDays(1), zone);
            var to = end < dayEnd ? end : dayEnd;
            if (to <= from) continue;

            var share = (decimal)((to - from).TotalSeconds / totalSeconds);
            Add(days, day, fees * share, rewards * share, missing);
        }
    }

    private static void Add(Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)> days, DateOnly day, decimal fees, decimal rewards, bool missing)
    {
        var existing = days.GetValueOrDefault(day);
        days[day] = (existing.Fees + fees, existing.Rewards + rewards, existing.Missing || missing);
    }

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    private static DateTime DayStartUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
