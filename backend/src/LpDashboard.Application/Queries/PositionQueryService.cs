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

    /// <summary>A specific (already normalized) owner, or every watched address when null.</summary>
    public async Task<IReadOnlyCollection<string>> ResolveOwnersAsync(string? owner, CancellationToken ct) =>
        owner is not null ? [owner] : (await addresses.ListAsync(ct)).Select(a => a.Address).ToList();

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
            UncollectedFeesUsd: m.Sum(x => x.UncollectedFeesUsd + x.PendingRewardsUsd),
            PnlUsd: m.Sum(x => x.PnlUsd),
            PnlVsHodlUsd: m.Sum(x => x.PnlVsHodlUsd),
            ImpermanentLossUsd: m.Sum(x => x.ImpermanentLossUsd),
            GasUsd: m.Sum(x => x.GasUsd),
            HasMissingPrices: m.Any(x => x.HasMissingPrices));
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

        var series = new List<PositionSeries>(list.Count);
        foreach (var p in list)
        {
            var points = snapshotsByPosition[p.Id].Select(s => new ValuePoint(s.Timestamp, s.ValueUsd, s.UncollectedUsd)).ToList();

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
            .Select(h => new PortfolioHistoryPointDto(h.Timestamp, h.ValueUsd, h.UnclaimedUsd, h.PositionCount))
            .ToList();
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

    private IReadOnlyList<DailyEarningsDto> SumDailyEarnings(
        IEnumerable<Position> list, ILookup<int, PositionSnapshot> snapshotsByPosition, TimeZoneInfo timeZone)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var totals = new Dictionary<DateOnly, (decimal Fees, decimal Rewards, bool Missing)>();
        foreach (var p in list)
        {
            var points = snapshotsByPosition[p.Id]
                .Select(s => new EarningsPoint(s.Timestamp, s.Uncollected0, s.Uncollected1, s.PendingReward, s.Price0Usd, s.Price1Usd, s.RewardPriceUsd))
                .ToList();

            // Snapshots are hourly; add the latest synced state so today is current (closed positions end at their close).
            var liveAt = p.Status == PositionStatus.Open ? p.ValuedAt : p.ClosedAt;
            if (liveAt is { } at && (points.Count == 0 || at > points[^1].Timestamp))
            {
                points.Add(new EarningsPoint(at, p.Uncollected0, p.Uncollected1, p.PendingReward, p.Price0Usd, p.Price1Usd, p.RewardPriceUsd));
            }

            foreach (var d in EarningsCalculator.Calculate(p.Events, points, timeZone))
            {
                var existing = totals.GetValueOrDefault(d.Date);
                totals[d.Date] = (existing.Fees + d.FeesUsd, existing.Rewards + d.RewardsUsd, existing.Missing || d.HasMissingPrices);
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
            result.Add(new DailyEarningsDto(day, t.Fees, t.Rewards, t.Fees + t.Rewards, t.Missing));
        }
        return result;
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
