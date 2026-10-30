using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Queries;

public sealed class PositionQueryService(
    IWatchedAddressRepository addresses,
    IPositionRepository positions,
    ISnapshotRepository snapshots,
    IPositionEventRepository events,
    IEnumerable<IPositionProvider> providers,
    PriceService prices,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PositionQueryService> logger)
{
    private const int MaxHistorySamples = 120;

    /// <summary>A specific (already normalized) owner, or every watched address when null.</summary>
    public async Task<IReadOnlyCollection<string>> ResolveOwnersAsync(string? owner, CancellationToken ct) =>
        owner is not null ? [owner] : (await addresses.ListAsync(ct)).Select(a => a.Address).ToList();

    public async Task<IReadOnlyList<PositionDto>> ListAsync(PositionFilter filter, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var list = await positions.QueryAsync(filter, ct);
        return list
            .Select(p => ToDto(p, now))
            .OrderByDescending(p => p.OpenedAt)
            .ThenByDescending(p => p.Id)
            .ToList();
    }

    public async Task<PositionDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        var position = await positions.GetAsync(id, ct);
        if (position is null) return null;

        var dto = ToDto(position, time.GetUtcNow().UtcDateTime);
        var eventDtos = CollectSplitter.Order(position.Events).Reverse().Select(ToEventDto).ToList();
        return new PositionDetailDto(dto, eventDtos);
    }

    public async Task<PortfolioSummaryDto> GetSummaryAsync(PositionFilter filter, CancellationToken ct)
    {
        var list = await ListAsync(filter, ct);
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

    public async Task<IReadOnlyList<SnapshotPointDto>?> GetSnapshotsAsync(int id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var position = await positions.GetAsync(id, ct);
        if (position is null) return null;

        var ordered = CollectSplitter.Order(position.Events).ToList();
        var points = await snapshots.ListAsync(id, null, null, ct);
        var result = new List<SnapshotPointDto>(points.Count);

        // Snapshots are only taken while syncing open positions, so reconstruct whatever came before the first one.
        var historyEnd = points.FirstOrDefault()?.Timestamp ?? position.ClosedAt ?? time.GetUtcNow().UtcDateTime;
        foreach (var h in await ReconstructHistoryAsync(position, ordered, historyEnd, ct))
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

    private async Task<IReadOnlyList<HistoryPoint>> ReconstructHistoryAsync(
        Position position, IReadOnlyList<PositionEvent> ordered, DateTime end, CancellationToken ct)
    {
        var t0 = position.Pool?.Token0;
        var t1 = position.Pool?.Token1;
        var firstDeposit = ordered.FirstOrDefault(e => e.Type == PositionEventType.IncreaseLiquidity);
        if (t0 is null || t1 is null || firstDeposit is null || end <= firstDeposit.Timestamp) return [];

        var samples = new List<PriceSample>();
        var provider = providers.FirstOrDefault(p => p.Chain == position.Chain && p.Protocol == position.Protocol);
        if (provider is not null)
        {
            const long hour = 3600;
            var start = new DateTimeOffset(DateTime.SpecifyKind(firstDeposit.Timestamp, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var stop = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var step = Math.Max(1, (long)Math.Ceiling((stop - start) / (double)(MaxHistorySamples * hour))) * hour;
            var first = start - start % hour + hour;
            var count = first < stop ? (int)((stop - 1 - first) / step) + 1 : 0;

            if (count > 0)
            {
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

        return PositionHistory.Build(ordered, position.TickLower, position.TickUpper, t0.Decimals, t1.Decimals, samples);
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

    private static PositionEventDto ToEventDto(PositionEvent e)
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

    private static PositionDto ToDto(Position p, DateTime now)
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
            metrics);
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
