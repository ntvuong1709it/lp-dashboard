using System.Numerics;
using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pools;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LpDashboard.Domain.Tests;

public class EntryPlanServiceTests
{
    private const string Weth = "0x4200000000000000000000000000000000000006";
    private const string Usdc = "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";
    private const string Cbbtc = "0xcbb7c0000ab88b473b1f5afd9ef808440eed33bf";

    private static readonly TokenInfo WethInfo = new(Weth, "WETH", 18);
    private static readonly TokenInfo UsdcInfo = new(Usdc, "USDC", 6);
    private static readonly TokenInfo CbbtcInfo = new(Cbbtc, "cbBTC", 8);

    // The static caches in PoolExplorerService and PriceService are keyed by pool and price key, so each test runs at a later
    // (fake) time than the last: stale caches are never reused across tests.
    private static long _hour;

    private sealed class Harness
    {
        public FakeProvider Provider { get; } = new();
        public FakePrices Prices { get; } = new();
        public ManualTime Time { get; } = new(DateTimeOffset.UtcNow.AddHours(Interlocked.Increment(ref _hour)));
        public EntryPlanService Plans { get; }
        public PoolExplorerService Pools { get; }

        public Harness(bool withProvider = true)
        {
            Pools = new PoolExplorerService(
                new FakePools(), Prices, null!, withProvider ? new IPositionProvider[] { Provider } : [], new PriceService(Prices, null!, Time, NullLogger<PriceService>.Instance),
                Time, NullLogger<PoolExplorerService>.Instance);
            Plans = new EntryPlanService(Pools);
            Prices.Set(Weth, 3400);
            Prices.Set(Usdc, 1);
            Prices.Set(Cbbtc, 98000);
            Provider.Add(WethInfo, UsdcInfo, 500, 10, 3400.0 * 1.0003);
            Provider.Add(UsdcInfo, CbbtcInfo, 500, 10, 1.0 / 98000);
            Provider.Add(WethInfo, CbbtcInfo, 500, 10, 3400.0 / 98000);
        }

        public async Task<EntryPlanDto> PlanAsync(string poolId, EntryPlanRequest request)
        {
            await Pools.ListAsync(new PoolQuery(), CancellationToken.None);
            return await Plans.PlanAsync(poolId, request, CancellationToken.None);
        }

        public async Task<EstimateException> FailAsync(string poolId, EntryPlanRequest request) =>
            await Assert.ThrowsAsync<EstimateException>(() => PlanAsync(poolId, request));
    }

    private static EntryPlanRequest Request(double budget = 5000, double lo = 10, double hi = 10, HedgeRequest? hedge = null, double income = 22) =>
        new(LowerPercent: lo, UpperPercent: hi, BudgetUsd: budget, IncomeAprPct: income, Hedge: hedge ?? new HedgeRequest("perp", 1, 8));

    [Fact]
    public async Task BudgetInTheStableToken_SwapsItIntoTheVolatileOne_AtThePoolPrice()
    {
        var h = new Harness();
        var plan = await h.PlanAsync("weth-usdc", Request());

        Assert.Equal(Weth, plan.Pool.Token0.Address);
        Assert.Equal(Usdc, plan.Pool.Token1.Address);
        Assert.Equal(3400.0 * 1.0003, plan.Pool.PoolPrice, 6);
        Assert.Equal(0.05m, plan.Pool.FeePercent);
        Assert.Equal(10, plan.Pool.TickSpacing);
        Assert.True(plan.PriceInRange);
        Assert.Equal(SwapSide.SellToken1, plan.Entry.Side);   // default: pay with the stable token
        Assert.Equal(Usdc, plan.SwapSellToken);
        Assert.Equal(Weth, plan.SwapBuyToken);
        Assert.Equal(0, plan.Range.TickLower % 10);
        Assert.Equal(0, plan.Range.TickUpper % 10);
        Assert.InRange(plan.Range.LowerPercent, -10.06, -9.94);
        Assert.InRange(plan.Entry.ValueUsd, 4990, 5000);
        Assert.Equal(EntryPlanService.DefaultGasUsd, plan.GasUsd);

        // Same answer as calling the planner directly on the numbers the response reports.
        var direct = EntryPlanner.Plan(0, 5000, new PoolPrices(plan.Pool.PoolPrice, 3400, 1), plan.Range, 0.001, plan.GasUsd, 48_200_000, 18, 6);
        Assert.Equal(direct.Amount0, plan.Entry.Amount0, 9);
        Assert.Equal(direct.Amount1, plan.Entry.Amount1, 6);
    }

    [Fact]
    public async Task Hedge_IsPlannedForTheVolatileLeg_WithScenarios()
    {
        var h = new Harness();
        var plan = await h.PlanAsync("weth-usdc", Request(hedge: new HedgeRequest("perp", 1, 8, 3, 0.05)));

        var hedge = plan.Hedge!;
        Assert.Equal(HedgedLegs.Token0, hedge.Legs);
        Assert.Equal(0, Assert.Single(hedge.Shorts).Token);
        Assert.Equal(plan.Entry.Amount0, hedge.Shorts[0].Amount, 9);
        Assert.Equal(31, hedge.Scenarios.Count);
        Assert.True(hedge.CarryPerDayUsd > 0);               // positive funding is earned by the short
        Assert.Equal(22.0 / 100, hedge.UnhedgedApr, 9);
    }

    [Fact]
    public async Task NoHedgeBlock_GivesNoHedge()
    {
        var h = new Harness();
        var request = Request() with { Hedge = null };
        Assert.Null((await h.PlanAsync("weth-usdc", request)).Hedge);
    }

    [Fact]
    public async Task Holdings_AreMatchedByAddress_InTheCallersOrder()
    {
        var h = new Harness();
        var request = new EntryPlanRequest(
            LowerPercent: 10, UpperPercent: 10,
            Holdings: [new HoldingRequest(Usdc.ToUpperInvariant().Replace("0X", "0x"), 800), new HoldingRequest(Weth, 1.2)]);

        var plan = await h.PlanAsync("weth-usdc", request);

        Assert.Equal(SwapSide.SellToken0, plan.Entry.Side);   // 1.2 WETH ($4,080) vs $800: too much WETH for a +/-10% range
        Assert.Equal(1.2 * 3400 + 800, plan.Entry.StartValueUsd, 6);
    }

    [Fact]
    public async Task VolatileSecondToken_DefaultsToPayingWithTheFirst_AndHedgesTokenOne()
    {
        // USDC sorts before cbBTC, so the stable token is token0 and the volatile one token1.
        var h = new Harness();
        var plan = await h.PlanAsync("usdc-cbbtc", Request());

        Assert.Equal(Usdc, plan.Pool.Token0.Address);
        Assert.Equal(Cbbtc, plan.Pool.Token1.Address);
        Assert.Equal(SwapSide.SellToken0, plan.Entry.Side);
        Assert.Equal(HedgedLegs.Token1, plan.Hedge!.Legs);
        Assert.Equal(1, plan.Hedge.Shorts[0].Token);
        Assert.Equal(31, plan.Hedge.Scenarios.Count);
        Assert.Equal(plan.Entry.Amount1, plan.Hedge.Shorts[0].Amount, 12);
    }

    [Fact]
    public async Task VolatilePair_HasShortsPerLeg_AndNoScenarios()
    {
        var h = new Harness();
        var plan = await h.PlanAsync("weth-cbbtc", Request());

        Assert.Equal(HedgedLegs.Both, plan.Hedge!.Legs);
        Assert.Equal(2, plan.Hedge.Shorts.Count);
        Assert.Empty(plan.Hedge.Scenarios);
    }

    [Fact]
    public async Task PaysWith_CanBeSplitOrEitherTokenAddress()
    {
        var h = new Harness();
        var split = await h.PlanAsync("weth-usdc", Request() with { PaysWith = "split" });
        var weth = await h.PlanAsync("weth-usdc", Request() with { PaysWith = Weth });

        Assert.Equal(5000, split.Entry.StartValueUsd, 6);   // half WETH, half USDC: $5,000 in all
        Assert.Equal(SwapSide.SellToken0, weth.Entry.Side);
        Assert.Equal(Weth, weth.SwapSellToken);
    }

    [Fact]
    public async Task ExplicitTicks_AboveThePrice_GiveASingleTokenPlanAndFlagThePriceOutOfRange()
    {
        var h = new Harness();
        var state = await h.Pools.ResolvePoolStateAsync("weth-usdc", CancellationToken.None);
        var above = PositionMath.SnapTick(PositionMath.PriceToTickExact(state.PoolPrice * 1.2, 18, 6), 10);

        var plan = await h.Plans.PlanAsync("weth-usdc",
            new EntryPlanRequest(TickLower: above, TickUpper: above + 2000, BudgetUsd: 5000), CancellationToken.None);

        Assert.False(plan.PriceInRange);
        Assert.Equal(0, plan.Entry.Amount1);
        Assert.True(plan.Entry.Amount0 > 0);
        Assert.Null(plan.RequestedLowerPercent);
        Assert.True(plan.Range.LowerPercent > 15);
    }

    [Fact]
    public async Task PoolState_IsReadOnceWithinTheCacheWindow_AndAgainAfterIt()
    {
        var h = new Harness();
        await h.PlanAsync("weth-usdc", Request());
        await h.Plans.PlanAsync("weth-usdc", Request(budget: 100), CancellationToken.None);
        Assert.Equal(1, h.Provider.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(11));
        await h.Plans.PlanAsync("weth-usdc", Request(budget: 200), CancellationToken.None);
        Assert.Equal(2, h.Provider.Calls);
    }

    [Fact]
    public async Task PricesAsOf_ReportsWhenThePricesWereFetched()
    {
        var h = new Harness();
        var first = await h.PlanAsync("weth-usdc", Request());
        h.Time.Advance(TimeSpan.FromSeconds(30));
        var second = await h.Plans.PlanAsync("weth-usdc", Request(budget: 100), CancellationToken.None);

        // 30 s later the pool is read again (10 s cache) but the USD prices are still the cached ones (60 s).
        Assert.Equal(first.Pool.PricesAsOf, second.Pool.PricesAsOf);
        Assert.True(second.Pool.ReadAt > first.Pool.ReadAt);
    }

    public static IEnumerable<object[]> BadRequests()
    {
        yield return [Request() with { LowerPercent = null, UpperPercent = null }, "range"];
        yield return [Request() with { TickLower = -1, TickUpper = 1 }, "range"];
        yield return [Request() with { UpperPercent = null }, "both"];
        yield return [Request() with { BudgetUsd = null }, "starting point"];
        yield return [Request() with { Holdings = [new HoldingRequest(Weth, 1)] }, "starting point"];
        yield return [Request() with { BudgetUsd = 0 }, "amount"];
        yield return [Request() with { BudgetUsd = -5 }, "BudgetUsd"];
        yield return [Request() with { SwapCostPct = 11 }, "SwapCostPct"];
        yield return [Request() with { PaysWith = "0xnotatoken" }, "paysWith"];
        yield return [Request(hedge: new HedgeRequest("perp", 1.5, 8)), "ratio"];
        yield return [Request(hedge: new HedgeRequest("perp", 1, 8, 0)), "leverage"];
        yield return [Request(hedge: new HedgeRequest("swap", 1, 8)), "instrument"];
        yield return [new EntryPlanRequest(LowerPercent: 10, UpperPercent: 10, Holdings: [new HoldingRequest("0x1234", 1)]), "not one of"];
        yield return [Request() with { BudgetUsd = double.NaN }, "BudgetUsd"];
        yield return [new EntryPlanRequest(TickLower: -195_000, TickUpper: -195_040, BudgetUsd: 100), "upper tick"];
    }

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task BadInput_IsRejectedAsInvalidRequest(EntryPlanRequest request, string messagePart)
    {
        var h = new Harness();
        var ex = await h.FailAsync("weth-usdc", request);
        Assert.Equal(EstimateFailure.InvalidRequest, ex.Failure);
        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownPool_IsNotFound()
    {
        var ex = await new Harness().FailAsync("nope", Request());
        Assert.Equal(EstimateFailure.PoolNotFound, ex.Failure);
    }

    [Fact]
    public async Task NoChainProvider_ExplainsWhy()
    {
        var ex = await new Harness(withProvider: false).FailAsync("weth-usdc", Request());
        Assert.Equal(EstimateFailure.NoChainData, ex.Failure);
        Assert.Contains("provider", ex.Message);
    }

    [Fact]
    public async Task PoolMissingOnChain_OrUnpriced_IsReportedAsNoChainData()
    {
        var h = new Harness();
        h.Provider.Clear();
        var missing = await h.FailAsync("weth-usdc", Request());
        Assert.Equal(EstimateFailure.NoChainData, missing.Failure);

        var h2 = new Harness();
        h2.Prices.Remove(Cbbtc);
        var unpriced = await h2.FailAsync("weth-cbbtc", Request());
        Assert.Equal(EstimateFailure.NoChainData, unpriced.Failure);
        Assert.Contains("cbBTC", unpriced.Message);
    }

    [Fact]
    public async Task ProviderFailure_IsReportedAsNoChainData_NotAsACrash()
    {
        var h = new Harness();
        h.Provider.Throw = true;
        var ex = await h.FailAsync("weth-usdc", Request());
        Assert.Equal(EstimateFailure.NoChainData, ex.Failure);
    }

    // --- fakes ---------------------------------------------------------------------------------------------------

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakePrices : IPriceSource
    {
        private readonly Dictionary<string, decimal> _prices = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string token, decimal usd) => _prices[token] = usd;
        public void Remove(string token) => _prices.Remove(token);

        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.Where(_prices.ContainsKey).ToDictionary(k => k, k => _prices[k], StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakePools : IPoolStatsSource
    {
        public Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct)
        {
            // The source lists tokens in its own order; the service must not rely on it matching the chain's.
            IReadOnlyList<PoolStats> pools =
            [
                new("weth-usdc", Chain.Base, Protocol.UniswapV3, "WETH-USDC", Usdc, Weth, 0.05m, null, 48_200_000m, 61_000_000m, 427_000_000m, null, null),
                new("usdc-cbbtc", Chain.Base, Protocol.UniswapV3, "USDC-cbBTC", Usdc, Cbbtc, 0.05m, null, 31_700_000m, 27_900_000m, 195_000_000m, null, null),
                new("weth-cbbtc", Chain.Base, Protocol.UniswapV3, "WETH-cbBTC", Weth, Cbbtc, 0.05m, null, 6_500_000m, 4_100_000m, 28_700_000m, null, null),
            ];
            return Task.FromResult(pools);
        }

        public Task<IReadOnlyList<PoolChartPoint>> GetChartAsync(string poolId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PoolChartPoint>>([]);
    }

    private sealed class FakeProvider : IPositionProvider
    {
        private readonly Dictionary<string, PoolChainState> _pools = [];

        public int Calls { get; private set; }
        public bool Throw { get; set; }

        public Chain Chain => Chain.Base;
        public Protocol Protocol => Protocol.UniswapV3;
        public string NativePriceKey => "native";
        public string GetPriceKey(string tokenAddress) => tokenAddress.ToLowerInvariant();

        public void Clear() => _pools.Clear();

        /// <summary>A pool whose token0 is <paramref name="a"/> if its address sorts first, like a factory would order them.</summary>
        public void Add(TokenInfo a, TokenInfo b, int fee, int spacing, double humanPrice)
        {
            var (t0, t1) = string.CompareOrdinal(a.Address, b.Address) <= 0 ? (a, b) : (b, a);
            var sqrt = Math.Sqrt(humanPrice * Math.Pow(10, t1.Decimals - t0.Decimals)) * Math.Pow(2, 96);
            var tick = PositionMath.SqrtPriceToTick(sqrt / Math.Pow(2, 96));
            _pools[Key(a.Address, b.Address)] = new PoolChainState($"0xpool{_pools.Count}", t0, t1, fee, spacing, new BigInteger(sqrt), tick);
        }

        private static string Key(string a, string b) =>
            string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()) <= 0 ? $"{a}|{b}".ToLowerInvariant() : $"{b}|{a}".ToLowerInvariant();

        public Task<PoolChainState?> ResolvePoolAsync(string tokenA, string tokenB, int? fee, int? tickSpacing, CancellationToken ct)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("rpc down");
            return Task.FromResult(_pools.GetValueOrDefault(Key(tokenA, tokenB)));
        }

        public Task<long> GetLatestBlockAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null) => throw new NotSupportedException();
        public Task<PositionChainState> GetStateAsync(string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct) => throw new NotSupportedException();
    }
}
