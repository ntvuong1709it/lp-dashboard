using LpDashboard.Application;
using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Borrow;
using LpDashboard.Application.Pools;
using LpDashboard.Application.Sync;
using LpDashboard.Domain;
using LpDashboard.Domain.Metrics;
using LpDashboard.Infrastructure;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LpDashboard.Domain.Tests;

/// <summary>Real borrow services, repositories and a throwaway SQLite file, with a fake Aave market and fixed prices.</summary>
public sealed class BorrowIntegrationTests : IDisposable
{
    private const string Owner = "0xabc0000000000000000000000000000000000002";
    private const string Weth = "0xweth";
    private const string Usdc = "0xusdc";
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lpdash-borrow-{Guid.NewGuid():N}.db");
    private readonly FakeLending _lending = new();
    private readonly ServiceProvider _services;

    public BorrowIntegrationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(config);
        services.AddApplication();
        services.Configure<SyncOptions>(o => o.SnapshotIntervalMinutes = 60);
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ILendingProvider>(_lending);
        services.AddSingleton<IPriceSource, FixedPrices>();
        services.AddSingleton<IPoolStatsSource, NoPools>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Sync_StoresAccountReservesAndDailyAccrual()
    {
        _lending.SetUpTwoDays();

        var errors = await SyncAsync();

        Assert.Empty(errors);
        await using var db = NewDb();
        var account = await db.LendingAccounts.SingleAsync();
        Assert.Equal(1.7m, account.HealthFactor);
        Assert.Equal(new DateOnly(2026, 11, 6), account.HistoryThrough);

        var days = await db.LendingDays.OrderBy(d => d.Reserve).ThenBy(d => d.Date).ToListAsync();
        var debtDays = days.Where(d => d.Reserve == Usdc).ToList();
        Assert.Equal(3, debtDays.Count); // two history days and today
        Assert.Equal(0m, debtDays[0].InterestTokens); // nothing before the first day to accrue on
        // Day two: 10,000 owed at index 1.00 -> 1.001 earned 10 of interest.
        Assert.Equal(10m, Math.Round(debtDays[1].InterestTokens, 6));
        // Collateral: 10 WETH at 1.00 -> 1.0001 earned 0.001.
        Assert.Equal(0.001m, Math.Round(days.Where(d => d.Reserve == Weth).ElementAt(1).YieldTokens, 6));
    }

    [Fact]
    public async Task Sync_Twice_DoesNotDuplicateDays()
    {
        _lending.SetUpTwoDays();

        await SyncAsync();
        await SyncAsync();

        await using var db = NewDb();
        Assert.Equal(6, await db.LendingDays.CountAsync());
    }

    [Fact]
    public async Task OpenDayRow_IsReplacedByItsClose()
    {
        _lending.SetUpTwoDays();
        await SyncAsync();

        // The next day the stored "today" row becomes yesterday and is rewritten from the close.
        _lending.Clock = new DateOnly(2026, 11, 8);
        _lending.Days.Add(Day(new DateOnly(2026, 11, 7), Usdc, 0, 10_000, 1m, 1.0015m));
        _lending.Days.Add(Day(new DateOnly(2026, 11, 7), Weth, 10, 0, 1.0002m, 1m));
        await SyncAsync(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc));

        await using var db = NewDb();
        var debt = await db.LendingDays.Where(d => d.Reserve == Usdc && d.Date == new DateOnly(2026, 11, 7)).ToListAsync();
        Assert.Single(debt);
        Assert.Equal(4.995005m, Math.Round(debt[0].InterestTokens, 6)); // 10,000 * (1.0015 / 1.001 - 1)
    }

    [Fact]
    public async Task Summary_ShowsTheAaveLoanItsCostAndHealth()
    {
        _lending.SetUpTwoDays();
        await SyncAsync();

        var summary = await QueryAsync(q => q.GetSummaryAsync([Owner], 7, TimeZoneInfo.Utc, default));
        var loans = await QueryAsync(q => q.GetLoansAsync([Owner], 7, TimeZoneInfo.Utc, default));

        var loan = Assert.Single(loans);
        Assert.Equal("aave", loan.Kind);
        Assert.Equal("USDC variable debt", loan.Label);
        Assert.Equal(10_000m, loan.DebtUsd);
        Assert.Equal("HealthFactor", loan.Risk.Kind);
        Assert.Equal(1.7m, loan.Risk.Value);

        // 6% a year on 10,000.
        Assert.Equal(LoanCostCalculator.Cost(10_000m, 6m).PerDay, loan.Cost.PerDayUsd);
        Assert.Equal(loan.Cost.PerDayUsd, summary.RunRate.InterestPerDayUsd);
        Assert.True(summary.HasLoans);
        Assert.Equal(loan.Cost.PerYearUsd, summary.Costs.Total.PerYearUsd);
        // History day two earned 10; today's open day earned 10,000 * (1.0015 / 1.001 - 1).
        Assert.Equal(14.995005m, Math.Round(summary.Coverage.InterestUsd, 6));
        Assert.Equal(20_000m, summary.Trend.CollateralUsd); // 10 WETH at $2,000
    }

    [Fact]
    public async Task Changes_SplitCollateralIntoPriceYieldAndDeposits()
    {
        _lending.SetUpTwoDays();
        await SyncAsync();

        var changes = await QueryAsync(q => q.GetChangesAsync([Owner], 2, TimeZoneInfo.Utc, "aave", default));

        Assert.Equal(
            changes.CollateralEndUsd - changes.CollateralStartUsd,
            changes.PriceMovesUsd + changes.DepositsUsd + changes.SupplyYieldUsd);
        Assert.Equal(
            changes.DebtEndUsd - changes.DebtStartUsd,
            changes.InterestUsd + changes.BorrowedUsd + changes.DebtPriceMovesUsd);
        Assert.True(changes.SupplyYieldUsd > 0);
        Assert.True(changes.InterestUsd > 0);
    }

    [Fact]
    public async Task FirstDayNotFound_FallsBackThenRetriesAndReadsFromTheTrueFirstDay()
    {
        _lending.SetUpTwoDays();
        _lending.Activity = null;

        await SyncAsync();

        await using (var db = NewDb())
        {
            var account = await db.LendingAccounts.SingleAsync();
            Assert.Equal(new DateOnly(2026, 8, 9), account.HistoryFrom); // 90 days back
            Assert.StartsWith("The wallet's first Aave v3 day could not be found", account.HistoryNote);
        }

        // The lookup works on the next run (e.g. the transfer history became available): history restarts at the real first day, earlier than the fallback.
        _lending.Activity = new LendingActivity(new DateOnly(2026, 8, 1), [Weth, Usdc]);
        var errors = await SyncAsync();

        Assert.Empty(errors);
        await using var after = NewDb();
        var fixedAccount = await after.LendingAccounts.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 1), fixedAccount.HistoryFrom);
        Assert.Equal(new DateOnly(2026, 11, 6), fixedAccount.HistoryThrough);
        Assert.Null(fixedAccount.HistoryNote);
        Assert.Equal(6, await after.LendingDays.CountAsync()); // no duplicates from re-reading
    }

    [Fact]
    public async Task UnreadableHistory_StopsBeforeTheFirstBadDayAndSaysWhy()
    {
        _lending.SetUpTwoDays();
        _lending.Failed = [new DateOnly(2026, 11, 6)];
        _lending.Note = "The RPC node cannot serve historical state.";

        await SyncAsync();

        await using var db = NewDb();
        var account = await db.LendingAccounts.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 5), account.HistoryThrough);
        Assert.Equal("The RPC node cannot serve historical state.", account.HistoryNote);

        var summary = await QueryAsync(q => q.GetSummaryAsync([Owner], 7, TimeZoneInfo.Utc, default));
        Assert.Contains("The RPC node cannot serve historical state.", summary.DataQuality.HistoryNotes);
    }

    [Fact]
    public async Task NoReadableHistory_DoesNotTurnTodaysBalanceIntoADeposit()
    {
        _lending.SetUpTwoDays();
        _lending.Failed = [new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6)];
        _lending.Note = "The RPC node cannot serve historical state.";

        await SyncAsync();

        var changes = await QueryAsync(q => q.GetChangesAsync([Owner], 7, TimeZoneInfo.Utc, "aave", default));
        Assert.Equal(0m, changes.DepositsUsd);
        Assert.Equal(0m, changes.BorrowedUsd);
        Assert.Equal(20_000m, changes.CollateralStartUsd); // flat at today's balance, not zero
        var history = await QueryAsync(q => q.GetCollateralHistoryAsync([Owner], 7, TimeZoneInfo.Utc, "aave", default));
        Assert.Empty(history.Events);
    }

    [Fact]
    public async Task WalletThatNeverUsedAave_LeavesNothingBehind()
    {
        _lending.Account = new LendingAccountState(1, 0, 0, 0, null, []);

        await SyncAsync();

        await using var db = NewDb();
        Assert.Equal(0, await db.LendingAccounts.CountAsync());
    }

    [Fact]
    public async Task ManualAndAaveLoans_AreCostedTogetherPerWallet()
    {
        _lending.SetUpTwoDays();
        await SyncAsync();
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ManualLoanService>().CreateAsync(
                new ManualLoanDto(0, Owner, "Binance loan", "Binance", "USDC", null, null, null, null, null, null,
                    [new TermDto(new DateOnly(2026, 10, 1), 5000m, 10m)], null), default);
        }

        var summary = await QueryAsync(q => q.GetSummaryAsync([Owner], 30, TimeZoneInfo.Utc, default));

        var wallet = Assert.Single(summary.Costs.Wallets);
        Assert.Equal(2, wallet.LoanKeys.Count);
        var expected = LoanCostCalculator.Cost(10_000m, 6m).PerYear + LoanCostCalculator.Cost(5000m, 10m).PerYear;
        Assert.Equal(expected, summary.Costs.Total.PerYearUsd);
        Assert.Equal(summary.Costs.Total.PerMonthUsd, expected / 12m);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> SyncAsync(DateTime? at = null)
    {
        await using var scope = _services.CreateAsyncScope();
        if (at is { } t) ((FixedClock)scope.ServiceProvider.GetRequiredService<TimeProvider>()).Set(t);
        return await scope.ServiceProvider.GetRequiredService<LendingSyncService>().SyncAsync(_lending, Owner, default);
    }

    private async Task<T> QueryAsync<T>(Func<BorrowQueryService, Task<T>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<BorrowQueryService>());
    }

    private AppDbContext NewDb() => _services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static LendingDayState Day(DateOnly date, string reserve, decimal supplied, decimal borrowed, decimal supplyIndex, decimal borrowIndex) =>
        new(date, reserve, reserve == Weth ? "WETH" : "USDC", reserve == Weth ? 18 : 6, $"base:{reserve}", supplied, borrowed, supplyIndex, borrowIndex, supplied > 0);

    public void Dispose()
    {
        _services.Dispose();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed class FixedClock(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start, TimeSpan.Zero);
        public void Set(DateTime utc) => _now = new DateTimeOffset(utc, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>WETH $2,000, everything else $1; every chart point answers the same.</summary>
    private sealed class FixedPrices : IPriceSource
    {
        private static decimal PriceOf(string key) => key.Contains("weth", StringComparison.OrdinalIgnoreCase) ? 2000m : 1m;

        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.ToDictionary(k => k, PriceOf, StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            GetCurrentAsync(keys, ct);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
            IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>>(keys.ToDictionary(
                k => k,
                k => (IReadOnlyList<PricePoint>)Enumerable.Range(0, count).Select(i => new PricePoint(start + i * stepSeconds, PriceOf(k))).ToList()));
    }

    private sealed class NoPools : IPoolStatsSource
    {
        public Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct) => Task.FromResult<IReadOnlyList<PoolStats>>([]);
        public Task<IReadOnlyList<PoolChartPoint>> GetChartAsync(string poolId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PoolChartPoint>>([]);
    }

    /// <summary>10 WETH supplied and 10,000 USDC borrowed at 6%; two days of history, then today.</summary>
    private sealed class FakeLending : ILendingProvider
    {
        public DateOnly Clock { get; set; } = new(2026, 11, 7);
        public Chain Chain => Domain.Chain.Base;
        public string Market => "Aave v3";
        public LendingAccountState? Account { get; set; }
        public List<LendingDayState> Days { get; } = [];
        public IReadOnlyList<DateOnly> Failed { get; set; } = [];
        public string? Note { get; set; }

        public void SetUpTwoDays()
        {
            Account = new LendingAccountState(
                100, 20_000m, 10_000m, 0.85m, 1.7m,
                [
                    new LendingReserveState(Weth, "WETH", 18, $"base:{Weth}", 10m, 0, 1m, 0, 1.0002m, 1m, true),
                    new LendingReserveState(Usdc, "USDC", 6, $"base:{Usdc}", 0, 10_000m, 0, 6m, 1m, 1.0015m, false)
                ]);
            Days.Clear();
            Days.Add(new LendingDayState(new DateOnly(2026, 11, 5), Weth, "WETH", 18, $"base:{Weth}", 10m, 0, 1.0m, 1m, true));
            Days.Add(new LendingDayState(new DateOnly(2026, 11, 5), Usdc, "USDC", 6, $"base:{Usdc}", 0, 10_000m, 1m, 1.0m, false));
            Days.Add(new LendingDayState(new DateOnly(2026, 11, 6), Weth, "WETH", 18, $"base:{Weth}", 10m, 0, 1.0001m, 1m, true));
            Days.Add(new LendingDayState(new DateOnly(2026, 11, 6), Usdc, "USDC", 6, $"base:{Usdc}", 0, 10_000m, 1m, 1.001m, false));
        }

        public Task<LendingAccountState?> GetAccountAsync(string owner, CancellationToken ct) => Task.FromResult(Account);

        /// <summary>Set to null to simulate an RPC that cannot find the wallet's first Aave day.</summary>
        public LendingActivity? Activity { get; set; } = new(new DateOnly(2026, 11, 5), [Weth, Usdc]);

        public Task<LendingActivity?> FindActivityAsync(string owner, CancellationToken ct) => Task.FromResult(Activity);

        public Task<LendingHistory> GetHistoryAsync(
            string owner, IReadOnlyCollection<string> reserves, DateOnly from, DateOnly to, CancellationToken ct, IProgress<ProviderProgress>? progress = null) =>
            Task.FromResult(new LendingHistory(
                Days.Where(d => d.Date >= from && d.Date <= to && reserves.Contains(d.Reserve, StringComparer.OrdinalIgnoreCase)).ToList(),
                Failed.Where(d => d >= from && d <= to).ToList(),
                Note));
    }
}
