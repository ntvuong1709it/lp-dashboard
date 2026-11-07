using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Application.Sync;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Queries;

public sealed class PositionQueryService(
    IWatchedAddressRepository addresses,
    IPositionRepository positions,
    ISnapshotRepository snapshots,
    IPositionEventRepository events,
    IEnumerable<IPositionProvider> providers,
    PriceService prices,
    LpDashboard.Application.Pools.PoolExplorerService pools,
    IUnitOfWork unitOfWork,
    IOptions<SyncOptions> syncOptions,
    TimeProvider time,
    ILogger<PositionQueryService> logger)
{
    private const int MaxHistorySamples = 120;

    /// <summary>Hourly samples up to 120 days. Time in range needs the finest data available: a brief dip out of range
    /// falls between coarser samples and would be missed entirely.</summary>
    private const int MaxTimeInRangeSamples = 24 * 120;

    /// <summary>Snapshots are normally one interval apart; a few missed ones are tolerated, longer holes are not measured.</summary>
    private TimeSpan MaxSnapshotGap => TimeSpan.FromMinutes(Math.Max(1, syncOptions.Value.SnapshotIntervalMinutes) * 3);

    /// <param name="withTimeInRange">
    /// Time in range loads price history for positions it has not seen before (cached afterwards), so callers that only
    /// need totals can skip it.
    /// </param>
    public async Task<IReadOnlyList<PositionDto>> ListAsync(PositionFilter filter, CancellationToken ct, bool withTimeInRange = true)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var list = await positions.QueryAsync(filter, ct);
        var snapshotsByPosition = withTimeInRange
            ? (await snapshots.ListLiteAsync(list.Select(p => p.Id).ToList(), ct)).ToLookup(s => s.PositionId)
            : null;

        // One at a time: the price cache writes through a single DbContext.
        var dtos = new List<PositionDto>(list.Count);
        foreach (var p in list)
        {
            var timeInRange = snapshotsByPosition is null ? null : await ComputeTimeInRangeAsync(p, snapshotsByPosition[p.Id], now, ct);
            dtos.Add(ToDto(p, now, timeInRange));
        }

        return dtos
            .OrderByDescending(p => p.OpenedAt)
            .ThenByDescending(p => p.Id)
            .ToList();
    }

    public async Task<PositionDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        var position = await positions.GetAsync(id, ct);
        if (position is null) return null;

        var now = time.GetUtcNow().UtcDateTime;
        var timeInRange = await ComputeTimeInRangeAsync(position, await snapshots.ListLiteAsync([id], ct), now, ct);
        var dto = ToDto(position, now, timeInRange);
        var eventDtos = CollectSplitter.Order(position.Events).Reverse().Select(ToEventDto).ToList();
        return new PositionDetailDto(dto, eventDtos);
    }

    /// <summary>Events of the matching positions, newest first, capped at <paramref name="limit"/>.</summary>
    public async Task<IReadOnlyList<ActivityEventDto>> ListActivityAsync(PositionFilter filter, int limit, CancellationToken ct) =>
        (await positions.QueryAsync(filter, ct))
            .SelectMany(p => p.Events.Select(e => (Position: p, Event: e)))
            .OrderByDescending(x => x.Event.Timestamp)
            .ThenByDescending(x => x.Event.BlockNumber)
            .ThenByDescending(x => x.Event.LogIndex)
            .Take(limit)
            .Select(x => new ActivityEventDto(
                x.Position.Id, x.Position.Chain, x.Position.Protocol, x.Position.TokenId,
                $"{x.Position.Pool?.Token0?.Symbol ?? "?"}/{x.Position.Pool?.Token1?.Symbol ?? "?"}",
                ToEventDto(x.Event)))
            .ToList();

    public async Task<PortfolioSummaryDto> GetSummaryAsync(PositionFilter filter, CancellationToken ct)
    {
        var list = await ListAsync(filter, ct, withTimeInRange: false);
        var m = list.Select(p => p.Metrics).ToList();
        return new PortfolioSummaryDto(
            OpenCount: list.Count(p => p.Status == PositionStatus.Open),
            ClosedCount: list.Count(p => p.Status == PositionStatus.Closed),
            CurrentValueUsd: m.Sum(x => x.CurrentValueUsd),
            DepositedUsd: m.Sum(x => x.DepositedUsd),
            FeesEarnedUsd: m.Sum(x => x.FeesEarnedUsd),
            UncollectedFeesUsd: m.Sum(x => x.UncollectedFeesUsd),
            PendingRewardsUsd: m.Sum(x => x.PendingRewardsUsd),
            PnlUsd: m.Sum(x => x.PnlUsd),
            PnlVsHodlUsd: m.Sum(x => x.PnlVsHodlUsd),
            ImpermanentLossUsd: m.Sum(x => x.ImpermanentLossUsd),
            GasUsd: m.Sum(x => x.GasUsd),
            HasMissingPrices: m.Any(x => x.HasMissingPrices));
    }

    /// <summary>Token exposure of open positions and reward-token results; reads stored state only, no price history.</summary>
    public async Task<PortfolioInsightsDto> GetPortfolioInsightsAsync(PositionFilter filter, CancellationToken ct)
    {
        var list = await positions.QueryAsync(filter, ct);
        return new PortfolioInsightsDto(PortfolioExposureCalculator.Calculate(list), RewardPnlCalculator.Calculate(list));
    }

    /// <summary>
    /// Rates that need more than the lifetime figures: trailing 7/30-day APR, APR per day in range, the pool comparison
    /// and a break-even against holding. Separate from the position list so a slow or unavailable pool source never
    /// delays the table.
    /// </summary>
    public async Task<PortfolioPerformanceDto> GetPerformanceAsync(PositionFilter filter, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var dtos = await ListAsync(filter, ct);
        var entities = (await positions.QueryAsync(filter, ct)).ToDictionary(p => p.Id);
        var open = entities.Values.Where(p => p.Status == PositionStatus.Open).ToList();

        var snapshotsByPosition = (await snapshots.ListForPositionsAsync(open.Select(p => p.Id).ToList(), ct)).ToLookup(s => s.PositionId);
        IReadOnlyDictionary<int, PoolRates> poolRates;
        try
        {
            poolRates = await pools.GetPoolRatesAsync(open, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load pool rates for the performance view");
            poolRates = new Dictionary<int, PoolRates>();
        }

        var rows = new List<PositionPerformanceDto>(dtos.Count);
        decimal totalRate = 0;
        var rateBases = new HashSet<string>();
        foreach (var p in dtos)
        {
            var m = p.Metrics;
            var inRangeApr = PerformanceCalculator.InRangeApr(m.FeeAprPercent, p.TimeInRange, m.AgeDays);

            if (p.Status != PositionStatus.Open || !entities.TryGetValue(p.Id, out var entity))
            {
                rows.Add(new PositionPerformanceDto(p.Id, inRangeApr, null, null, null, null));
                continue;
            }

            var snaps = snapshotsByPosition[p.Id].ToList();
            var daily = EarningsCalculator.Calculate(entity.Events, BuildEarningsPoints(entity, snaps), TimeZoneInfo.Utc);
            var values = snaps.Select(s => new ValueSample(s.Timestamp, s.ValueUsd)).ToList();
            var apr7 = PerformanceCalculator.Trailing(daily, values, m.CurrentValueUsd, m.AgeDays, today, 7);
            var apr30 = PerformanceCalculator.Trailing(daily, values, m.CurrentValueUsd, m.AgeDays, today, 30);

            // Recent rate where the position is old enough to have one, else its lifetime average.
            HodlBreakEven? breakEven = null;
            if (apr30 is not null || m.AgeDays is >= 1)
            {
                var (rate, basis) = apr30 is not null ? (apr30.EarnedUsd / apr30.Days, "30d") : (m.FeesEarnedUsd / (decimal)m.AgeDays!.Value, "lifetime");
                breakEven = PerformanceCalculator.BreakEven(m.PnlVsHodlUsd, rate, basis);
                totalRate += rate;
                rateBases.Add(basis);
            }

            var comparison = PerformanceCalculator.ComparePool(
                p.IsStaked, poolRates.GetValueOrDefault(p.Id), apr7, apr30, m.FeeAprPercent);
            rows.Add(new PositionPerformanceDto(p.Id, inRangeApr, apr7, apr30, comparison, breakEven));
        }

        // The gap of every position (closed ones included) against what the open ones earn per day.
        HodlBreakEven? portfolio = dtos.Count == 0
            ? null
            : PerformanceCalculator.BreakEven(
                dtos.Sum(p => p.Metrics.PnlVsHodlUsd), totalRate, rateBases.Count == 1 ? rateBases.First() : rateBases.Count == 0 ? "none" : "mixed");
        return new PortfolioPerformanceDto(rows, portfolio);
    }

    /// <summary>Snapshots as earnings points, plus the latest synced state so today is current.</summary>
    private static List<EarningsPoint> BuildEarningsPoints(Position p, IEnumerable<PositionSnapshot> snapshots)
    {
        var points = snapshots
            .Select(s => new EarningsPoint(s.Timestamp, s.Uncollected0, s.Uncollected1, s.PendingReward, s.Price0Usd, s.Price1Usd, s.RewardPriceUsd))
            .ToList();

        var liveAt = p.Status == PositionStatus.Open ? p.ValuedAt : p.ClosedAt;
        if (liveAt is { } at && (points.Count == 0 || at > points[^1].Timestamp))
        {
            points.Add(new EarningsPoint(at, p.Uncollected0, p.Uncollected1, p.PendingReward, p.Price0Usd, p.Price1Usd, p.RewardPriceUsd));
        }
        return points;
    }

    /// <summary>
    /// Measured from snapshots where they exist; the rest of the position's life (before the first sync, gaps while the
    /// app was off) is estimated from the tick implied by historical token prices. Falls back to measured-only if the
    /// price history cannot be loaded.
    /// </summary>
    private async Task<TimeInRange?> ComputeTimeInRangeAsync(Position p, IEnumerable<SnapshotLite> snaps, DateTime now, CancellationToken ct)
    {
        var measured = snaps.Select(s => new TickSample(s.Timestamp, s.CurrentTick)).ToList();

        // Include the latest synced state so an open position's current stretch counts.
        if (p.Status == PositionStatus.Open && p.ValuedAt is { } valuedAt && p.CurrentTick is { } tick
            && (measured.Count == 0 || valuedAt > measured.Max(s => s.Timestamp)))
        {
            measured.Add(new TickSample(valuedAt, tick));
        }

        var estimated = new List<TickSample>();
        var estimatedGap = MaxSnapshotGap;
        try
        {
            var end = p.Status == PositionStatus.Closed ? p.ClosedAt ?? now : now;
            var history = await ReconstructHistoryAsync(p, CollectSplitter.Order(p.Events).ToList(), end, MaxTimeInRangeSamples, ct);
            estimated.AddRange(history.Points.Select(h => new TickSample(h.Timestamp, h.CurrentTick)));
            // Points exist only while liquidity does, so a hole wider than the sampling step means the position was empty.
            estimatedGap = history.Step * 2 + TimeSpan.FromHours(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not estimate time in range from price history for position {PositionId}", p.Id);
        }

        return TimeInRangeCalculator.Calculate(measured, estimated, p.TickLower, p.TickUpper, MaxSnapshotGap, estimatedGap);
    }

    /// <summary>Total value of the matching positions over time, from snapshots (history starts at the first sync).</summary>
    public async Task<IReadOnlyList<PortfolioHistoryPointDto>> GetPortfolioHistoryAsync(
        PositionFilter filter, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var list = await positions.QueryAsync(filter, ct);
        var snapshotsByPosition = (await snapshots.ListLiteAsync(list.Select(p => p.Id).ToList(), ct)).ToLookup(s => s.PositionId);

        // One at a time: reconstruction writes the price cache through a single DbContext.
        var series = new List<PositionSeries>(list.Count);
        foreach (var p in list)
        {
            var points = snapshotsByPosition[p.Id].Select(s => new ValuePoint(s.Timestamp, s.ValueUsd, s.UncollectedUsd)).ToList();

            // Snapshots start at the first sync, so fill the time before it from events and historical prices.
            // Without this a newly added wallet appears out of nowhere and the total jumps.
            var historyEnd = points.Count > 0 ? points.Min(x => x.Timestamp) : p.Status == PositionStatus.Closed ? p.ClosedAt : p.ValuedAt;
            if (historyEnd is { } reconstructUntil)
            {
                try
                {
                    var history = await ReconstructHistoryAsync(p, CollectSplitter.Order(p.Events).ToList(), reconstructUntil, MaxHistorySamples, ct);
                    points.AddRange(history.Points
                        .Where(h => h.Timestamp < reconstructUntil)
                        .Select(h => new ValuePoint(h.Timestamp, h.ValueUsd, 0, Estimated: true)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not reconstruct portfolio history for position {PositionId}", p.Id);
                }
            }

            // Add the latest synced valuation (same formula as a snapshot) so the chart ends at the current value.
            if (p.Status == PositionStatus.Open && p.ValuedAt is { } valuedAt && (points.Count == 0 || valuedAt > points.Max(x => x.Timestamp)))
            {
                var value = p.CurrentAmount0 * (p.Price0Usd ?? 0) + p.CurrentAmount1 * (p.Price1Usd ?? 0);
                var unclaimed = p.Uncollected0 * (p.Price0Usd ?? 0) + p.Uncollected1 * (p.Price1Usd ?? 0)
                    + p.PendingReward * (p.RewardPriceUsd ?? 0);
                points.Add(new ValuePoint(valuedAt, value, unclaimed));
            }

            series.Add(new PositionSeries(points, p.Status == PositionStatus.Closed ? p.ClosedAt : null));
        }

        return PortfolioHistoryCalculator.Build(series, from, to)
            .Select(h => new PortfolioHistoryPointDto(h.Timestamp, h.ValueUsd, h.UnclaimedUsd, h.PositionCount, h.Estimated))
            .ToList();
    }

    /// <summary>Cumulative and monthly P/L of the matching positions, using reconstructed value history from the first deposit.</summary>
    public async Task<PnlHistoryDto> GetPnlHistoryAsync(PositionFilter filter, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var list = await positions.QueryAsync(filter, ct);

        // One at a time: reconstruction writes the price cache through a single DbContext.
        var series = new List<PnlSeries>(list.Count);
        foreach (var p in list)
        {
            var points = await GetSnapshotsAsync(p.Id, null, null, ct) ?? [];
            var flows = new List<PnlFlow>();
            foreach (var e in CollectSplitter.Order(p.Events))
            {
                var gas = e.GasUsd ?? 0;
                var usd = e.Type switch
                {
                    PositionEventType.IncreaseLiquidity => -(e.Amount0 * (e.Price0Usd ?? 0) + e.Amount1 * (e.Price1Usd ?? 0)),
                    PositionEventType.DecreaseLiquidity => e.Principal0 * (e.Price0Usd ?? 0) + e.Principal1 * (e.Price1Usd ?? 0),
                    _ => 0m,
                };
                if (usd - gas != 0) flows.Add(new PnlFlow(e.Timestamp, usd - gas));
            }

            var finalAt = p.Status == PositionStatus.Closed ? p.ClosedAt ?? now : now;
            series.Add(new PnlSeries(
                points.Select(x => new ValuePoint(x.Timestamp, x.ValueWithFeesUsd, 0)).ToList(),
                flows,
                finalAt,
                ToDto(p, now, null).Metrics.PnlUsd));
        }

        var history = PnlHistoryCalculator.Build(series, now);
        return new(
            history.Points.Select(x => new PnlPointDto(x.Timestamp, x.CumulativePnlUsd)).ToList(),
            history.Months.Select(x => new MonthlyPnlDto(x.Month, x.NetPnlUsd)).ToList());
    }

    public async Task<IReadOnlyList<SnapshotPointDto>?> GetSnapshotsAsync(int id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var position = await positions.GetAsync(id, ct);
        if (position is null) return null;

        var ordered = CollectSplitter.Order(position.Events).ToList();
        var points = await snapshots.ListAsync(id, null, null, ct);
        var result = new List<SnapshotPointDto>(points.Count);

        // Snapshots are only taken while syncing open positions, so reconstruct whatever came before the first one.
        var historyEnd = points.FirstOrDefault()?.Timestamp ?? position.ClosedAt ?? time.GetUtcNow().UtcDateTime;
        foreach (var h in (await ReconstructHistoryAsync(position, ordered, historyEnd, MaxHistorySamples, ct)).Points)
        {
            if (points.Count == 0 || h.Timestamp < historyEnd)
                result.Add(new SnapshotPointDto(h.Timestamp, h.ValueUsd, h.ValueWithFeesUsd, h.HodlValueUsd, h.Price0Usd, h.Price1Usd, h.CurrentTick));
        }

        foreach (var s in points)
        {
            decimal dep0 = 0, dep1 = 0, claimedUsd = 0;
            foreach (var e in ordered.TakeWhile(e => e.Timestamp <= s.Timestamp))
            {
                switch (e.Type)
                {
                    case PositionEventType.IncreaseLiquidity:
                        dep0 += e.Amount0;
                        dep1 += e.Amount1;
                        break;
                    case PositionEventType.Collect:
                        claimedUsd += e.Fee0 * (e.Price0Usd ?? 0) + e.Fee1 * (e.Price1Usd ?? 0);
                        break;
                    case PositionEventType.RewardClaim:
                        claimedUsd += e.RewardAmount * (e.RewardPriceUsd ?? 0);
                        break;
                }
            }

            var hodl = dep0 * (s.Price0Usd ?? 0) + dep1 * (s.Price1Usd ?? 0);
            result.Add(new SnapshotPointDto(s.Timestamp, s.ValueUsd, s.ValueUsd + s.UncollectedUsd + claimedUsd, hodl, s.Price0Usd, s.Price1Usd, s.CurrentTick));
        }

        return result
            .Where(p => (from is null || p.Timestamp >= from) && (to is null || p.Timestamp < to))
            .OrderBy(p => p.Timestamp)
            .ToList();
    }

    /// <param name="Step">Spacing of the price samples; zero when none were taken.</param>
    private sealed record Reconstruction(IReadOnlyList<HistoryPoint> Points, TimeSpan Step);

    /// <param name="maxSamples">Upper bound on price samples; the spacing grows beyond one hour only when the span needs more.</param>
    private async Task<Reconstruction> ReconstructHistoryAsync(
        Position position, IReadOnlyList<PositionEvent> ordered, DateTime end, int maxSamples, CancellationToken ct)
    {
        var t0 = position.Pool?.Token0;
        var t1 = position.Pool?.Token1;
        var firstDeposit = ordered.FirstOrDefault(e => e.Type == PositionEventType.IncreaseLiquidity);
        if (t0 is null || t1 is null || firstDeposit is null || end <= firstDeposit.Timestamp) return new([], TimeSpan.Zero);

        var samples = new List<PriceSample>();
        var stepSpan = TimeSpan.Zero;
        var provider = providers.FirstOrDefault(p => p.Chain == position.Chain && p.Protocol == position.Protocol);
        if (provider is not null)
        {
            const long hour = 3600;
            var start = new DateTimeOffset(DateTime.SpecifyKind(firstDeposit.Timestamp, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var stop = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var step = Math.Max(1, (long)Math.Ceiling((stop - start) / (double)(maxSamples * hour))) * hour;
            var first = start - start % hour + hour;
            var count = first < stop ? (int)((stop - 1 - first) / step) + 1 : 0;

            if (count > 0)
            {
                stepSpan = TimeSpan.FromSeconds(step);
                var key0 = provider.GetPriceKey(t0.Address).ToLowerInvariant();
                var key1 = provider.GetPriceKey(t1.Address).ToLowerInvariant();
                var series = await prices.GetSeriesAsync([key0, key1], first, step, count, ct);
                for (var i = 0; i < count; i++)
                {
                    var ts = first + i * step;
                    if (series.GetValueOrDefault(new PriceQuery(key0, ts)) is { } p0 && series.GetValueOrDefault(new PriceQuery(key1, ts)) is { } p1)
                        samples.Add(new PriceSample(DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime, p0, p1));
                }

                try
                {
                    await unitOfWork.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Only the price cache is written here; a concurrent request may have cached the same buckets.
                    logger.LogWarning(ex, "Could not cache history prices for position {PositionId}", position.Id);
                }
            }
        }

        return new(PositionHistory.Build(ordered, position.TickLower, position.TickUpper, t0.Decimals, t1.Decimals, samples), stepSpan);
    }

    public async Task<IReadOnlyList<WeeklyClaimsDto>> GetWeeklyClaimsAsync(ClaimFilter filter, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var claims = await events.ListClaimsAsync(filter, ct);
        var items = claims
            .Where(e => e.Type == PositionEventType.RewardClaim || e.Fee0 > 0 || e.Fee1 > 0)
            .Select(ToClaimDto)
            .ToList();

        return items
            .GroupBy(i => WeekStart(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(i.Timestamp, DateTimeKind.Utc), timeZone)))
            .OrderByDescending(g => g.Key)
            .Select(g => new WeeklyClaimsDto(
                g.Key,
                g.Sum(i => i.FeesUsd),
                g.Sum(i => i.RewardsUsd),
                g.Sum(i => i.TotalUsd),
                g.Count(),
                g.OrderByDescending(i => i.Timestamp).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<DailyEarningsDto>> GetDailyEarningsAsync(PositionFilter filter, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var list = await positions.QueryAsync(filter, ct);
        var snapshotsByPosition = (await snapshots.ListForPositionsAsync(list.Select(p => p.Id).ToList(), ct))
            .ToLookup(s => s.PositionId);
        return SumDailyEarnings(list, snapshotsByPosition, timeZone);
    }

    public async Task<IReadOnlyList<DailyEarningsDto>?> GetPositionDailyEarningsAsync(int id, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var position = await positions.GetAsync(id, ct);
        if (position is null) return null;

        var points = await snapshots.ListAsync(id, null, null, ct);
        return SumDailyEarnings([position], points.ToLookup(s => s.PositionId), timeZone);
    }

    public async Task<IReadOnlyList<PositionEarningsDto>> GetEarningsByPositionAsync(PositionFilter filter, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var list = await positions.QueryAsync(filter, ct);
        var snapshotsByPosition = (await snapshots.ListForPositionsAsync(list.Select(p => p.Id).ToList(), ct))
            .ToLookup(s => s.PositionId);

        var today = EarningsCalculator.LocalDate(time.GetUtcNow().UtcDateTime, timeZone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Windows end yesterday so a half-finished day does not drag the averages down; month to date includes today.
        static decimal Sum(IEnumerable<DailyEarnings> days, DateOnly from, DateOnly to) =>
            days.Where(d => d.Date >= from && d.Date <= to).Sum(d => d.FeesUsd + d.RewardsUsd);

        return list
            .Select(p =>
            {
                var days = EarningsCalculator.Calculate(p.Events, BuildEarningsPoints(p, snapshotsByPosition[p.Id]), timeZone);
                var yesterday = today.AddDays(-1);
                DateOnly? lastEarned = days.Where(d => d.FeesUsd + d.RewardsUsd > 0).Select(d => (DateOnly?)d.Date).LastOrDefault();
                return new PositionEarningsDto(
                    p.Id,
                    p.TokenId,
                    p.Protocol,
                    $"{p.Pool?.Token0?.Symbol ?? "?"}/{p.Pool?.Token1?.Symbol ?? "?"}",
                    p.Status,
                    Sum(days, yesterday, yesterday),
                    Sum(days, today.AddDays(-7), yesterday),
                    Sum(days, today.AddDays(-30), yesterday),
                    Sum(days, monthStart, today),
                    days.Sum(d => d.FeesUsd + d.RewardsUsd),
                    lastEarned,
                    p.Owner);
            })
            .OrderByDescending(e => e.Last30Usd)
            .ToList();
    }

    private IReadOnlyList<DailyEarningsDto> SumDailyEarnings(
        IEnumerable<Position> list, ILookup<int, PositionSnapshot> snapshotsByPosition, TimeZoneInfo timeZone)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var totals = new Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)>();
        var tokens = new Dictionary<DateOnly, Dictionary<string, TokenEarningDto>>();
        foreach (var p in list)
        {
            // Snapshots are hourly; the latest synced state is added so today is current (closed positions end at their close).
            var points = BuildEarningsPoints(p, snapshotsByPosition[p.Id]);

            foreach (var d in EarningsCalculator.Calculate(p.Events, points, timeZone))
            {
                var existing = totals.GetValueOrDefault(d.Date);
                totals[d.Date] = (existing.Fees + d.FeesUsd, existing.Rewards + d.RewardsUsd, existing.Missing || d.HasMissingPrices);

                if (!tokens.TryGetValue(d.Date, out var byToken)) tokens[d.Date] = byToken = [];
                AddToken(byToken, "fee", p.Pool?.Token0?.Address, p.Pool?.Token0?.Symbol, d.Fee0, d.Fee0Usd);
                AddToken(byToken, "fee", p.Pool?.Token1?.Address, p.Pool?.Token1?.Symbol, d.Fee1, d.Fee1Usd);
                AddToken(byToken, "reward", p.RewardTokenAddress, p.RewardTokenSymbol, d.RewardAmount, d.RewardsUsd);
            }
        }

        if (totals.Count == 0) return [];

        // Fill quiet days so the series is continuous up to today.
        var today = EarningsCalculator.LocalDate(now, timeZone);
        var first = totals.Keys.Min();
        var last = totals.Keys.Max() > today ? totals.Keys.Max() : today;
        var result = new List<DailyEarningsDto>();
        for (var day = last; day >= first; day = day.AddDays(-1))
        {
            var t = totals.GetValueOrDefault(day);
            var dayTokens = tokens.TryGetValue(day, out var byToken)
                ? byToken.Values.OrderByDescending(x => x.Usd).ToList()
                : [];
            result.Add(new DailyEarningsDto(day, t.Fees, t.Rewards, t.Fees + t.Rewards, t.Missing, dayTokens));
        }
        return result;
    }

    /// <summary>Groups by token address (symbol when there is none), keeping fees and rewards of the same token apart.</summary>
    private static void AddToken(Dictionary<string, TokenEarningDto> byToken, string kind, string? address, string? symbol, decimal amount, decimal usd)
    {
        if (amount <= 0 && usd <= 0) return;
        var token = (!string.IsNullOrWhiteSpace(address) ? address : symbol ?? "?").Trim().ToLowerInvariant();
        var key = $"{kind}:{token}";
        byToken[key] = byToken.TryGetValue(key, out var e)
            ? e with { Amount = e.Amount + amount, Usd = e.Usd + usd }
            : new TokenEarningDto(symbol ?? "?", address, kind, amount, usd);
    }

    private static DateOnly WeekStart(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static FeeClaimItemDto ToClaimDto(PositionEvent e)
    {
        var position = e.Position!;
        var t0 = position.Pool?.Token0;
        var t1 = position.Pool?.Token1;
        var missing = (e.Fee0 > 0 && e.Price0Usd is null) || (e.Fee1 > 0 && e.Price1Usd is null) || (e.RewardAmount > 0 && e.RewardPriceUsd is null);
        var feesUsd = e.Fee0 * (e.Price0Usd ?? 0) + e.Fee1 * (e.Price1Usd ?? 0);
        var rewardsUsd = e.RewardAmount * (e.RewardPriceUsd ?? 0);

        return new FeeClaimItemDto(
            position.Id,
            position.TokenId,
            position.Protocol,
            $"{t0?.Symbol ?? "?"}/{t1?.Symbol ?? "?"}",
            e.Type,
            e.TxHash,
            e.Timestamp,
            e.Fee0,
            e.Fee1,
            t0?.Symbol,
            t1?.Symbol,
            e.RewardAmount,
            e.RewardTokenSymbol,
            feesUsd,
            rewardsUsd,
            feesUsd + rewardsUsd,
            e.GasUsd ?? 0,
            missing);
    }

    internal static PositionEventDto ToEventDto(PositionEvent e)
    {
        decimal? value = e.Type switch
        {
            PositionEventType.RewardClaim => e.RewardAmount * e.RewardPriceUsd,
            _ => e.Price0Usd is null || e.Price1Usd is null ? null : e.Amount0 * e.Price0Usd + e.Amount1 * e.Price1Usd
        };

        return new PositionEventDto(
            e.Id, e.Type, e.TxHash, e.BlockNumber, e.Timestamp,
            e.Amount0, e.Amount1, e.Principal0, e.Principal1, e.Fee0, e.Fee1,
            e.RewardTokenSymbol, e.RewardAmount,
            e.Price0Usd, e.Price1Usd, e.RewardPriceUsd, value, e.GasUsd);
    }

    private static PositionDto ToDto(Position p, DateTime now, TimeInRange? timeInRange)
    {
        var t0 = p.Pool?.Token0;
        var t1 = p.Pool?.Token1;

        var valuation = p.Status == PositionStatus.Open
            ? new PositionValuation(p.CurrentAmount0, p.CurrentAmount1, p.Uncollected0, p.Uncollected1, p.PendingReward, p.Price0Usd, p.Price1Usd, p.RewardPriceUsd)
            : null;
        var metrics = PositionMetricsCalculator.Calculate(p.Events, valuation, p.OpenedAt, p.ClosedAt, now);

        return new PositionDto(
            p.Id,
            p.Chain,
            p.Protocol,
            p.TokenId,
            p.Owner,
            p.Status,
            p.IsStaked,
            p.Pool?.Address,
            p.Pool?.Fee,
            p.Pool?.TickSpacing,
            t0 is null ? null : new TokenDto(t0.Address, t0.Symbol, t0.Decimals),
            t1 is null ? null : new TokenDto(t1.Address, t1.Symbol, t1.Decimals),
            p.TickLower,
            p.TickUpper,
            p.CurrentTick,
            t0 is null || t1 is null ? null : BuildRange(p, t0.Decimals, t1.Decimals),
            p.OpenedAt,
            p.ClosedAt,
            p.CurrentAmount0,
            p.CurrentAmount1,
            p.Uncollected0,
            p.Uncollected1,
            p.PendingReward,
            p.RewardTokenSymbol,
            p.Price0Usd,
            p.Price1Usd,
            p.ValuedAt,
            metrics,
            timeInRange);
    }

    private static RangeDto BuildRange(Position p, int decimals0, int decimals1)
    {
        var lower = PositionMath.TickToPrice(p.TickLower, decimals0, decimals1);
        var upper = PositionMath.TickToPrice(p.TickUpper, decimals0, decimals1);
        double? current = p.SqrtPriceX96 is { } sqrt && !sqrt.IsZero
            ? PositionMath.SqrtPriceX96ToPrice(sqrt, decimals0, decimals1)
            : null;
        bool? inRange = p.CurrentTick is { } tick ? PositionMath.IsInRange(tick, p.TickLower, p.TickUpper) : null;

        return new RangeDto(
            lower,
            upper,
            current,
            inRange,
            current is > 0 ? (current - lower) / current * 100 : null,
            current is > 0 ? (upper - current) / current * 100 : null);
    }
}
