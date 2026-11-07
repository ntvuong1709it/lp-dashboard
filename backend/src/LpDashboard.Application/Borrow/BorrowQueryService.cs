using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Application.Queries;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Borrow;

/// <summary>
/// Joins what the loans cost with what the LP positions earn. LP income is the Earnings page's own accrued figures, so there is
/// one definition of it. Windows are local days ending today; loan balances are sampled once per day.
/// </summary>
public sealed class BorrowQueryService(
    IManualLoanRepository manualLoans,
    ILendingRepository lending,
    IPositionRepository positions,
    PositionQueryService positionQueries,
    PriceService prices,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<BorrowQueryService> logger)
{
    public const int MaxDays = 365;
    private const long DaySeconds = 86400;

    // ---- endpoints -------------------------------------------------------------------------------------------------

    public async Task<BorrowSummaryDto> GetSummaryAsync(IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, CancellationToken ct)
    {
        var ctx = await LoadAsync(owners, days, zone, ct);
        var active = ctx.Loans.Where(l => l.Active).ToList();

        var coverage = CoverageOver(ctx, ctx.WindowFrom, ctx.Today);
        var last7 = CoverageOver(ctx, ctx.Today.AddDays(-6), ctx.Today);

        var interestPerDay = active.Sum(l => l.Cost.PerDay);
        var lpPerDay = (last7.LpFeesUsd + last7.LpRewardsUsd) / 7m;
        // LP income is the last 7 days' pace; supply yield is today's balance at today's rate (like the interest), so a gap in
        // the stored Aave history cannot hide it.
        var supplyPerDay = ctx.SupplyYieldPerDayUsd;
        var incomePerDay = lpPerDay + supplyPerDay;
        var runRate = new RunRateDto(
            interestPerDay,
            incomePerDay,
            lpPerDay,
            ctx.OpenValueUsd,
            ctx.OpenValueUsd > 0 ? interestPerDay * 365m / ctx.OpenValueUsd * 100m : null,
            ctx.OpenValueUsd > 0 ? lpPerDay * 365m / ctx.OpenValueUsd * 100m : null);

        var income = LoanCost.FromPerDay(incomePerDay);
        var total = LoanCostCalculator.Sum(active.Select(l => l.Cost));
        var lpIncome = LoanCost.FromPerDay(lpPerDay);
        var supplyIncome = LoanCost.FromPerDay(supplyPerDay);
        var costs = new CostsDto(
            active.GroupBy(l => l.Owner)
                .Select(g => new WalletCostsDto(g.Key, g.Select(l => l.Key).ToList(), ToDto(LoanCostCalculator.Sum(g.Select(l => l.Cost)))))
                .ToList(),
            ToDto(total),
            ToDto(lpIncome),
            ToDto(supplyIncome),
            ToDto(income),
            ToDto(new LoanCost(income.PerDay - total.PerDay, income.PerWeek - total.PerWeek, income.PerMonth - total.PerMonth, income.PerYear - total.PerYear)));

        var worst = active.Where(l => l.Risk.Kind != "None")
            .OrderByDescending(l => l.Risk.Level)
            .ThenBy(l => l.Risk.DropToLiquidationPercent ?? decimal.MaxValue)
            .FirstOrDefault();

        var repay = BorrowCoverageCalculator.Repay(
            new Coverage(coverage.LpFeesUsd, coverage.LpRewardsUsd, coverage.SupplyYieldUsd, coverage.InterestUsd, coverage.GasUsd),
            ctx.Days,
            active.Select(l => new LoanRate(l.Label, l.AprPercent)).ToList());

        var series = CollateralTrendCalculator.Series(SelectLegs(ctx, "all"), ctx.Baseline, ctx.Today);
        var now = series.Count > 0 ? series[^1] : null;
        var start = series.Count > 0 ? series[0] : null;
        var excluded = active.Where(l => !ctx.Legs.Any(g => g.Key == l.LegKey)).Select(l => l.Label).ToList();

        return new BorrowSummaryDto(
            ctx.WindowFrom, ctx.Today, ctx.Days, active.Count > 0,
            ToDto(coverage), runRate, costs,
            worst?.Risk, worst?.Label,
            new RepayDto(repay.Mode, repay.AmountUsd, repay.LoanLabel, repay.AprPercent, repay.SavingPerYearUsd),
            new TrendHeadlineDto(now?.CollateralUsd ?? 0, now?.DebtUsd ?? 0, now?.Ratio, start?.Ratio, now?.LtvPercent, excluded),
            new DataQualityDto(ctx.MissingPriceKeys.Count > 0, ctx.MissingPriceKeys, ctx.HistoryNotes));
    }

    public async Task<IReadOnlyList<DailyCoverageDto>> GetDailyAsync(IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, CancellationToken ct)
    {
        var ctx = await LoadAsync(owners, days, zone, ct);
        var result = new List<DailyCoverageDto>();
        for (var day = ctx.WindowFrom; day <= ctx.Today; day = day.AddDays(1))
        {
            var c = CoverageOver(ctx, day, day);
            result.Add(new DailyCoverageDto(day, c.LpFeesUsd, c.LpRewardsUsd, c.SupplyYieldUsd, c.InterestUsd, c.GasUsd, c.NetCarryUsd));
        }
        return result;
    }

    public async Task<IReadOnlyList<LoanDto>> GetLoansAsync(IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, CancellationToken ct)
    {
        var ctx = await LoadAsync(owners, days, zone, ct);
        return ctx.Loans.Where(l => l.Active).Select(l =>
        {
            decimal? ratio = null, change = null;
            var legs = ctx.Legs.Where(g => g.Key == l.LegKey).Select(g => g.Leg).ToList();
            if (legs.Count > 0)
            {
                var series = CollateralTrendCalculator.Series(legs, ctx.Baseline, ctx.Today);
                ratio = series[^1].Ratio;
                change = ratio is { } now && series[0].Ratio is { } before ? now - before : null;
            }

            var interest = l.InterestUsdByDay.Where(d => d.Key >= ctx.WindowFrom && d.Key <= ctx.Today).Sum(d => d.Value);
            return new LoanDto(
                l.Key, l.Kind, l.Source, l.Label, l.Owner, l.DebtSymbol, l.ManualId, l.DebtTokens, l.DebtUsd, l.AprPercent,
                interest, ToDto(l.Cost), l.CollateralUsd, ratio, change, l.Risk, l.HasMissingPrices);
        }).ToList();
    }

    /// <param name="loanKey">"all", a kind ("aave", "manual") or the key of one manual loan.</param>
    public async Task<CollateralHistoryDto> GetCollateralHistoryAsync(
        IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, string? loanKey, CancellationToken ct)
    {
        var ctx = await LoadAsync(owners, days, zone, ct);
        var legs = SelectLegs(ctx, loanKey);
        var points = CollateralTrendCalculator.Series(legs, ctx.Baseline, ctx.Today)
            .Select(p => new TrendPointDto(p.Date, p.CollateralUsd, p.DebtUsd, p.Ratio, p.LtvPercent))
            .ToList();
        var events = CollateralTrendCalculator.Events(legs, ctx.Baseline, ctx.Today)
            .Select(e => new TrendEventDto(e.Date, e.Kind, e.Usd, e.Label))
            .ToList();

        // One loan (or one Aave account) has a single warning level and liquidation point to draw.
        decimal? warn = null, liquidation = null;
        var chosen = ctx.Legs.Where(g => Matches(g, loanKey)).ToList();
        if (chosen.Select(g => g.Key).Distinct().Count() == 1 && chosen[0].LiquidationRatio is { } liq)
        {
            liquidation = liq;
            warn = chosen[0].WarnRatio;
        }

        var excluded = ctx.Loans.Where(l => l.Active && !ctx.Legs.Any(g => g.Key == l.LegKey)).Select(l => l.Label).ToList();
        return new CollateralHistoryDto(points, events, warn, liquidation, excluded);
    }

    public async Task<ChangesDto> GetChangesAsync(
        IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, string? loanKey, CancellationToken ct)
    {
        var ctx = await LoadAsync(owners, days, zone, ct);
        var s = CollateralTrendCalculator.Split(SelectLegs(ctx, loanKey), ctx.Baseline, ctx.Today);
        decimal? Ratio(decimal collateral, decimal debt) => debt > 0 ? collateral / debt : null;
        return new ChangesDto(
            s.From, s.To, s.CollateralStartUsd, s.PriceMovesUsd, s.DepositsUsd, s.SupplyYieldUsd, s.CollateralEndUsd,
            s.DebtStartUsd, s.InterestUsd, s.BorrowedUsd, s.DebtPriceMovesUsd, s.DebtEndUsd,
            Ratio(s.CollateralStartUsd, s.DebtStartUsd), Ratio(s.CollateralEndUsd, s.DebtEndUsd));
    }

    // ---- loading ---------------------------------------------------------------------------------------------------

    private sealed class LoanState
    {
        public required string Key { get; init; }
        public required string Kind { get; init; }
        public required string Source { get; init; }
        public required string Label { get; init; }
        public required string Owner { get; init; }
        public required string DebtSymbol { get; init; }
        /// <summary>Which trend legs show this loan's collateral: the loan itself, or its owner's Aave account.</summary>
        public required string LegKey { get; init; }
        public int? ManualId { get; init; }
        public bool Active { get; set; }
        public decimal DebtTokens { get; set; }
        public decimal DebtUsd { get; set; }
        public decimal AprPercent { get; set; }
        public LoanCost Cost { get; set; } = LoanCost.Zero;
        public decimal? CollateralUsd { get; set; }
        public LoanRiskDto Risk { get; set; } = new("None", null, RiskLevel.Ok, null, null, null);
        public bool HasMissingPrices { get; set; }
        public Dictionary<DateOnly, decimal> InterestUsdByDay { get; } = [];
    }

    private sealed class LegState
    {
        public required string Key { get; init; }
        public required string Kind { get; init; }
        public required TrendLeg Leg { get; init; }
        public decimal? WarnRatio { get; init; }
        public decimal? LiquidationRatio { get; init; }
    }

    private sealed class BorrowContext
    {
        public required DateOnly Today { get; init; }
        public required DateOnly WindowFrom { get; init; }
        public required DateOnly Baseline { get; init; }
        public required int Days { get; init; }
        public required List<LoanState> Loans { get; init; }
        public required List<LegState> Legs { get; init; }
        public required Dictionary<DateOnly, decimal> SupplyYieldUsd { get; init; }
        /// <summary>Aave supply yield per day at today's balances and supply rates.</summary>
        public required decimal SupplyYieldPerDayUsd { get; init; }
        public required Dictionary<DateOnly, DailyEarningsDto> Lp { get; init; }
        public required Dictionary<DateOnly, decimal> Gas { get; init; }
        public required decimal OpenValueUsd { get; init; }
        public required List<string> MissingPriceKeys { get; init; }
        public required List<string> HistoryNotes { get; init; }
    }

    private async Task<BorrowContext> LoadAsync(IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var now = time.GetUtcNow().UtcDateTime;
        var today = EarningsCalculator.LocalDate(now, zone);
        var baseline = today.AddDays(-days);

        var filter = new PositionFilter(owners);
        var manual = (await manualLoans.ListAsync(owners, ct)).ToList();
        var accounts = await lending.ListAccountsAsync(owners, ct);
        var reserves = await lending.ListReservesAsync(owners, ct);
        var lendingDays = await lending.ListDaysAsync(owners, DateOnly.MinValue, ct);

        var priceKeys = manual
            .SelectMany(l => new[] { l.AssetPriceKey, l.CollateralPriceKey })
            .Concat(reserves.Select(r => r.PriceKey))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!.ToLowerInvariant())
            .Distinct()
            .ToList();
        var book = await LoadPricesAsync(priceKeys, baseline, today, ct);

        var loans = new List<LoanState>();
        var legs = new List<LegState>();
        var supplyYield = new Dictionary<DateOnly, decimal>();
        var notes = new List<string>();
        decimal supplyPerDay = 0;

        foreach (var l in manual)
        {
            var (state, leg) = BuildManual(l, book, baseline, today);
            loans.Add(state);
            if (leg is not null) legs.Add(leg);
        }

        foreach (var account in accounts)
        {
            var mine = reserves.Where(r => r.Owner == account.Owner).ToList();
            var rows = lendingDays.Where(d => d.Owner == account.Owner).ToLookup(d => d.Reserve, StringComparer.OrdinalIgnoreCase);
            supplyPerDay += BuildAave(account, mine, rows, book, baseline, today, loans, legs, supplyYield);
            if (account.HistoryNote is { Length: > 0 } note) notes.Add(note);
        }

        var lp = (await positionQueries.GetDailyEarningsAsync(filter, zone, ct)).ToDictionary(d => d.Date);
        var all = await positions.QueryAsync(filter, ct);
        var gas = new Dictionary<DateOnly, decimal>();
        foreach (var e in all.SelectMany(p => p.Events))
        {
            if (e.GasUsd is not { } g || g == 0) continue;
            var date = EarningsCalculator.LocalDate(e.Timestamp, zone);
            gas[date] = gas.GetValueOrDefault(date) + g;
        }

        var openValue = all.Where(p => p.Status == PositionStatus.Open)
            .Sum(p => p.CurrentAmount0 * (p.Price0Usd ?? 0) + p.CurrentAmount1 * (p.Price1Usd ?? 0));

        return new BorrowContext
        {
            Today = today,
            WindowFrom = today.AddDays(-(days - 1)),
            Baseline = baseline,
            Days = days,
            Loans = loans,
            Legs = legs,
            SupplyYieldUsd = supplyYield,
            SupplyYieldPerDayUsd = supplyPerDay,
            Lp = lp,
            Gas = gas,
            OpenValueUsd = openValue,
            MissingPriceKeys = book.MissingKeys.Order().ToList(),
            HistoryNotes = notes
        };
    }

    private (LoanState Loan, LegState? Leg) BuildManual(ManualLoan l, PriceBook book, DateOnly baseline, DateOnly today)
    {
        var key = $"manual:{l.Id}";
        var state = new LoanState
        {
            Key = key,
            Kind = "manual",
            Source = $"Manual · {l.Platform}",
            Label = l.Label,
            Owner = l.Owner,
            DebtSymbol = l.AssetSymbol,
            LegKey = key,
            ManualId = l.Id
        };

        var terms = l.Terms.Select(t => new TermRow(t.EffectiveFrom, t.Principal, t.AprPercent)).ToList();
        var collateral = l.Collateral.Select(c => new AmountRow(c.EffectiveFrom, c.Amount)).ToList();
        var daily = ManualLoanAccrual.Daily(terms, collateral, l.ClosedOn, baseline, today);

        var debtDays = new List<BalanceDay>(daily.Count);
        var collateralDays = new List<BalanceDay>(daily.Count);
        foreach (var d in daily)
        {
            var debtPrice = book.At(l.AssetPriceKey, d.Date);
            debtDays.Add(new BalanceDay(d.Date, d.DebtTokens, debtPrice ?? 0, d.InterestTokens));
            if (d.InterestTokens > 0) state.InterestUsdByDay[d.Date] = d.InterestTokens * (debtPrice ?? 0);

            var collateralPrice = l.CollateralPriceKey is null ? null : book.At(l.CollateralPriceKey, d.Date);
            collateralDays.Add(new BalanceDay(d.Date, d.CollateralTokens, collateralPrice ?? 0, 0));
        }

        var hasCollateral = l.CollateralPriceKey is not null && l.Collateral.Count > 0;
        state.HasMissingPrices = book.IsMissing(l.AssetPriceKey) || (l.CollateralPriceKey is not null && book.IsMissing(l.CollateralPriceKey));

        decimal? warn = null, liquidation = null;
        if (ManualLoanAccrual.Current(terms, l.ClosedOn, today) is { } current)
        {
            var price = book.At(l.AssetPriceKey, today) ?? 0;
            state.Active = true;
            state.DebtTokens = current.DebtTokens;
            state.DebtUsd = current.DebtTokens * price;
            state.AprPercent = current.AprPercent;
            state.Cost = LoanCostCalculator.Cost(current.Principal * price, current.AprPercent);

            if (hasCollateral && daily.Count > 0)
            {
                var collateralUsd = daily[^1].CollateralTokens * (book.At(l.CollateralPriceKey!, today) ?? 0);
                state.CollateralUsd = collateralUsd;

                if (l.LiquidationLtvPercent is { } liq && collateralUsd > 0 && state.DebtUsd > 0)
                {
                    var ltv = state.DebtUsd / collateralUsd * 100m;
                    state.Risk = new LoanRiskDto(
                        "Ltv", ltv, BorrowCoverageCalculator.LtvLevel(ltv, liq),
                        BorrowCoverageCalculator.LtvDropToLiquidationPercent(ltv, liq), liq, l.MarginCallLtvPercent);
                    liquidation = 100m / liq;
                    warn = 100m / (liq * (1 - BorrowCoverageCalculator.LtvWarnShare));
                }
            }
        }

        var leg = hasCollateral
            ? new LegState { Key = key, Kind = "manual", Leg = new TrendLeg(l.Label, collateralDays, debtDays), WarnRatio = warn, LiquidationRatio = liquidation }
            : null;
        return (state, leg);
    }

    /// <summary>
    /// One Aave account becomes a loan per borrowed reserve (cost, interest, health factor) and a trend leg per reserve
    /// (supplied collateral and debt as daily balances). A day without a stored row keeps the last known balance.
    /// </summary>
    private static decimal BuildAave(
        LendingAccount account,
        List<LendingReserve> reserves,
        ILookup<string, LendingDay> rows,
        PriceBook book,
        DateOnly baseline,
        DateOnly today,
        List<LoanState> loans,
        List<LegState> legs,
        Dictionary<DateOnly, decimal> supplyYield)
    {
        var legKey = $"aave:{account.Owner}";
        var incompleteHistory = account.HistoryNote is { Length: > 0 };
        decimal? warn = null, liquidation = null;
        if (account.LiquidationThreshold > 0)
        {
            warn = BorrowCoverageCalculator.HealthWarn / account.LiquidationThreshold;
            liquidation = 1m / account.LiquidationThreshold;
        }

        var risk = account.HealthFactor is { } hf
            ? new LoanRiskDto("HealthFactor", hf, BorrowCoverageCalculator.HealthFactorLevel(hf),
                BorrowCoverageCalculator.HealthFactorDropToLiquidationPercent(hf), null, null)
            : new LoanRiskDto("None", null, RiskLevel.Ok, null, null, null);

        decimal supplyPerDay = 0;
        foreach (var r in reserves)
        {
            // Yield at today's balance and supply rate, the same basis as what the loans cost, so it does not depend on stored history.
            if (r.SuppliedTokens > 0)
                supplyPerDay += r.SuppliedTokens * (book.At(r.PriceKey, today) ?? 0) * r.SupplyAprPercent / 100m / 365m;

            var byDate = rows[r.Reserve].ToDictionary(d => d.Date);
            var supplied = new List<BalanceDay>();
            var borrowed = new List<BalanceDay>();
            var interestByDay = new Dictionary<DateOnly, decimal>();
            LendingDay? last = null;

            foreach (var d in byDate.Keys.Where(k => k < baseline).OrderBy(k => k)) last = byDate[d];
            // When history is incomplete (the note says why), days before the first stored one show that first balance instead of
            // zero, so today's balance does not read as a huge deposit on its first day.
            if (last is null && incompleteHistory && byDate.Count > 0) last = byDate[byDate.Keys.Min()];
            for (var day = baseline; day <= today; day = day.AddDays(1))
            {
                var price = book.At(r.PriceKey, day) ?? 0;
                if (byDate.TryGetValue(day, out var row))
                {
                    last = row;
                    supplied.Add(new BalanceDay(day, row.IsCollateral ? row.SuppliedTokens : 0, price, row.IsCollateral ? row.YieldTokens : 0));
                    borrowed.Add(new BalanceDay(day, row.BorrowedTokens, price, row.InterestTokens));
                    if (row.YieldTokens > 0) supplyYield[day] = supplyYield.GetValueOrDefault(day) + row.YieldTokens * price;
                    if (row.InterestTokens > 0) interestByDay[day] = row.InterestTokens * price;
                }
                else
                {
                    supplied.Add(new BalanceDay(day, last is { IsCollateral: true } ? last.SuppliedTokens : 0, price, 0));
                    borrowed.Add(new BalanceDay(day, last?.BorrowedTokens ?? 0, price, 0));
                }
            }

            if (supplied.Any(d => d.Tokens > 0) || borrowed.Any(d => d.Tokens > 0))
                legs.Add(new LegState { Key = legKey, Kind = "aave", Leg = new TrendLeg($"{r.Symbol} (Aave)", supplied, borrowed), WarnRatio = warn, LiquidationRatio = liquidation });

            if (r.BorrowedTokens <= 0) continue;

            var current = book.At(r.PriceKey, today) ?? 0;
            var debtUsd = r.BorrowedTokens * current;
            var loan = new LoanState
            {
                Key = $"aave:{account.Owner}:{r.Reserve}",
                Kind = "aave",
                Source = "Aave v3 · Base",
                Label = $"{r.Symbol} variable debt",
                Owner = account.Owner,
                DebtSymbol = r.Symbol,
                LegKey = legKey,
                Active = true,
                DebtTokens = r.BorrowedTokens,
                DebtUsd = debtUsd,
                AprPercent = r.BorrowAprPercent,
                Cost = LoanCostCalculator.Cost(debtUsd, r.BorrowAprPercent),
                CollateralUsd = account.TotalCollateralUsd,
                Risk = risk,
                HasMissingPrices = book.IsMissing(r.PriceKey)
            };
            foreach (var (day, usd) in interestByDay) loan.InterestUsdByDay[day] = usd;
            loans.Add(loan);
        }

        return supplyPerDay;
    }

    private async Task<PriceBook> LoadPricesAsync(IReadOnlyList<string> keys, DateOnly from, DateOnly today, CancellationToken ct)
    {
        var series = keys.ToDictionary(k => k, _ => new SortedList<DateOnly, decimal>());
        var current = new Dictionary<string, decimal>();
        if (keys.Count == 0) return new PriceBook(series, current, today, keys);

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var count = today.DayNumber - from.DayNumber + 1;
        try
        {
            var result = await prices.GetSeriesAsync(keys, start, DaySeconds, count, ct);
            foreach (var (query, price) in result)
            {
                if (price is not { } p || !series.TryGetValue(query.Key, out var list)) continue;
                list[DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(query.Timestamp).UtcDateTime)] = p;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load price history for loans");
        }

        try
        {
            foreach (var (key, price) in await prices.GetCurrentAsync(keys, ct)) current[key.ToLowerInvariant()] = price;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load current prices for loans");
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the price cache is written here; a concurrent request may have cached the same buckets.
            logger.LogWarning(ex, "Could not cache loan prices");
        }

        return new PriceBook(series, current, today, keys);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static Coverage CoverageOver(BorrowContext ctx, DateOnly from, DateOnly to)
    {
        decimal fees = 0, rewards = 0, supply = 0, interest = 0, gas = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (ctx.Lp.TryGetValue(day, out var lp))
            {
                fees += lp.FeesUsd;
                rewards += lp.RewardsUsd;
            }
            gas += ctx.Gas.GetValueOrDefault(day);
            supply += ctx.SupplyYieldUsd.GetValueOrDefault(day);
            foreach (var l in ctx.Loans) interest += l.InterestUsdByDay.GetValueOrDefault(day);
        }
        return new Coverage(fees, rewards, supply, interest, gas);
    }

    private static bool Matches(LegState leg, string? key) =>
        string.IsNullOrWhiteSpace(key) || key == "all" || key == leg.Key || key == leg.Kind;

    private static IReadOnlyList<TrendLeg> SelectLegs(BorrowContext ctx, string? key) =>
        ctx.Legs.Where(g => Matches(g, key)).Select(g => g.Leg).ToList();

    private static CoverageDto ToDto(Coverage c) =>
        new(c.LpFeesUsd, c.LpRewardsUsd, c.SupplyYieldUsd, c.InterestUsd, c.GasUsd, c.LpIncomeUsd, c.NetCarryUsd, c.Ratio, c.Status);

    private static LoanCostDto ToDto(LoanCost c) => new(c.PerDay, c.PerWeek, c.PerMonth, c.PerYear);

    /// <summary>Daily prices by key. A day without a price uses the nearest earlier one (else the nearest later); a key with none is reported missing.</summary>
    private sealed class PriceBook(
        IReadOnlyDictionary<string, SortedList<DateOnly, decimal>> series,
        IReadOnlyDictionary<string, decimal> current,
        DateOnly today,
        IReadOnlyCollection<string> keys)
    {
        private readonly HashSet<string> _missing = [];

        public IReadOnlyCollection<string> MissingKeys => _missing;

        public bool IsMissing(string key) => _missing.Contains(key.ToLowerInvariant());

        public decimal? At(string key, DateOnly day)
        {
            key = key.ToLowerInvariant();
            if (day == today && current.TryGetValue(key, out var live)) return live;

            if (series.TryGetValue(key, out var list) && list.Count > 0)
            {
                decimal? earlier = null;
                foreach (var (date, price) in list)
                {
                    if (date <= day) earlier = price;
                    else return earlier ?? price;
                }
                return earlier;
            }

            if (keys.Contains(key)) _missing.Add(key);
            return null;
        }
    }
}
