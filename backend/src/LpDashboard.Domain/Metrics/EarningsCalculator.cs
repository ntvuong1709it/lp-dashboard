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

/// <summary>
/// What accrued on one local day. The token amounts (<see cref="Fee0"/>, <see cref="Fee1"/>, <see cref="RewardAmount"/>) are in the
/// position's own tokens; the USD figures are those amounts priced at accrual time, with <see cref="FeesUsd"/> = fee0 + fee1 USD.
/// </summary>
public sealed record DailyEarnings(DateOnly Date, decimal FeesUsd, decimal RewardsUsd, bool HasMissingPrices)
{
    public decimal Fee0 { get; init; }
    public decimal Fee1 { get; init; }
    public decimal Fee0Usd { get; init; }
    public decimal Fee1Usd { get; init; }
    public decimal RewardAmount { get; init; }
}

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

    /// <summary>Token amounts and their USD value that accrued in one interval or day.</summary>
    private readonly record struct Accrual(decimal Fee0, decimal Fee1, decimal Fee0Usd, decimal Fee1Usd, decimal Reward, decimal RewardUsd, bool Missing)
    {
        public bool HasAmounts => Fee0 > 0 || Fee1 > 0 || Reward > 0;

        public Accrual Scale(decimal share) =>
            new(Fee0 * share, Fee1 * share, Fee0Usd * share, Fee1Usd * share, Reward * share, RewardUsd * share, Missing);

        public Accrual Plus(Accrual o) =>
            new(Fee0 + o.Fee0, Fee1 + o.Fee1, Fee0Usd + o.Fee0Usd, Fee1Usd + o.Fee1Usd, Reward + o.Reward, RewardUsd + o.RewardUsd, Missing || o.Missing);

        public static Accrual Fees(decimal fee0, decimal fee1, decimal? price0, decimal? price1) =>
            new(fee0, fee1, fee0 * (price0 ?? 0), fee1 * (price1 ?? 0), 0, 0,
                (fee0 > 0 && price0 is null) || (fee1 > 0 && price1 is null));

        public static Accrual Rewards(decimal amount, decimal? price) =>
            new(0, 0, 0, 0, amount, amount * (price ?? 0), amount > 0 && price is null);
    }

    public static IReadOnlyList<DailyEarnings> Calculate(IEnumerable<PositionEvent> events, IEnumerable<EarningsPoint> points, TimeZoneInfo zone)
    {
        var claims = events
            .Where(e => e.Type is PositionEventType.Collect or PositionEventType.RewardClaim)
            .OrderBy(e => e.Timestamp)
            .ToList();
        var ordered = points.OrderBy(p => p.Timestamp).ToList();

        var days = new Dictionary<DateOnly, Accrual>();
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

                var accrual = Accrual.Fees(d0, d1, p0, p1).Plus(Accrual.Rewards(dr, pr));
                if (accrual.HasAmounts || accrual.Missing)
                {
                    Spread(days, previousPoint.Timestamp, point.Timestamp, accrual, zone);
                }
            }

            previousPoint = point;
            previous = current;
        }

        return days
            .OrderBy(d => d.Key)
            .Select(d => new DailyEarnings(d.Key, d.Value.Fee0Usd + d.Value.Fee1Usd, d.Value.RewardUsd, d.Value.Missing)
            {
                Fee0 = d.Value.Fee0,
                Fee1 = d.Value.Fee1,
                Fee0Usd = d.Value.Fee0Usd,
                Fee1Usd = d.Value.Fee1Usd,
                RewardAmount = d.Value.Reward
            })
            .ToList();
    }

    private static void BackfillFromClaims(
        Dictionary<DateOnly, Accrual> days,
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
                Spread(days, lastCollect, e.Timestamp, Accrual.Fees(e.Fee0, e.Fee1, e.Price0Usd, e.Price1Usd), zone);
                lastCollect = e.Timestamp;
            }
            else
            {
                Spread(days, lastReward, e.Timestamp, Accrual.Rewards(e.RewardAmount, e.RewardPriceUsd), zone);
                lastReward = e.Timestamp;
            }
        }

        // What was already unclaimed when tracking began accrued since the last claim (unknown start: skip).
        if (first is null) return;
        if (lastCollect < first.Timestamp)
        {
            Spread(days, lastCollect, first.Timestamp, Accrual.Fees(first.Uncollected0, first.Uncollected1, first.Price0Usd, first.Price1Usd), zone);
        }
        if (lastReward < first.Timestamp)
        {
            Spread(days, lastReward, first.Timestamp, Accrual.Rewards(first.PendingReward, first.RewardPriceUsd), zone);
        }
    }

    /// <summary>Distributes an interval's earnings over the local days it covers, proportionally to time.</summary>
    private static void Spread(Dictionary<DateOnly, Accrual> days, DateTime start, DateTime end, Accrual accrual, TimeZoneInfo zone)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        end = DateTime.SpecifyKind(end, DateTimeKind.Utc);
        var totalSeconds = (end - start).TotalSeconds;
        var first = LocalDate(start, zone);
        var last = LocalDate(end, zone);

        if (totalSeconds <= 0 || first == last)
        {
            Add(days, last, accrual);
            return;
        }

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var from = start > DayStartUtc(day, zone) ? start : DayStartUtc(day, zone);
            var dayEnd = DayStartUtc(day.AddDays(1), zone);
            var to = end < dayEnd ? end : dayEnd;
            if (to <= from) continue;

            var share = (decimal)((to - from).TotalSeconds / totalSeconds);
            Add(days, day, accrual.Scale(share));
        }
    }

    private static void Add(Dictionary<DateOnly, Accrual> days, DateOnly day, Accrual accrual) =>
        days[day] = days.TryGetValue(day, out var existing) ? existing.Plus(accrual) : accrual;

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    private static DateTime DayStartUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
