using System.Numerics;
using LpLedger.Core;
using LpLedger.Core.Aerodrome;
using LpLedger.Core.Models;
using LpLedger.Infrastructure.Chain;
using LpLedger.Infrastructure.Demo;
using static LpLedger.Core.ConcentratedLiquidityMath;

namespace LpLedger.Tests;

public class AerodromeAdapterTests
{
    private const string Weth = "0x4200000000000000000000000000000000000006";
    private const string Usdc = "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";
    private const string Owner = "0x1111111111111111111111111111111111111111";
    private const string Other = "0x2222222222222222222222222222222222222222";
    private const string Gauge = "0x3333333333333333333333333333333333333333";

    private static int TickFor(double usdcPerEth) => (int)Math.Round(Math.Log(usdcPerEth * 1e-12) / Math.Log(1.0001) / 100) * 100;
    private static readonly int Lower = TickFor(2900), Upper = TickFor(3700);
    private static BigInteger SqrtX96(double usdcPerEth) => new(Math.Sqrt(usdcPerEth * 1e-12) * Math.Pow(2, 96));
    private static readonly BigInteger L = new(1e18 / (1 / Math.Sqrt(3500e-12) - 1 / TickToSqrtPrice(Upper)));

    private static RawPosition Raw(BigInteger liquidity) => new(Weth, Usdc, 100, Lower, Upper, liquidity);

    private const string Manager = "0x4857db576ee0e45f10525ec59527bfd10beec60b";
    private static PositionRef Ref(int id) => new(Manager, id);

    private static RawLiquidityLog Log(int id, EventKind kind, long block, BigInteger a0, BigInteger a1) =>
        new(kind, Ref(id), a0, a1, block, "0xabc", 0);

    private sealed class FakeReader(
        Dictionary<int, string?> owners, Dictionary<int, RawPosition?> positions, List<RawLiquidityLog> logs, bool staked = false) : IChainReader
    {
        public Task<IReadOnlyList<PositionRef>> CandidatesAsync(string owner) =>
            Task.FromResult<IReadOnlyList<PositionRef>>(owners.Keys.Select(Ref).ToList());
        public Task<IReadOnlyList<RawLiquidityLog>> LiquidityLogsAsync(string owner, IReadOnlyList<PositionRef> refs) =>
            Task.FromResult<IReadOnlyList<RawLiquidityLog>>(logs);
        public Task<IReadOnlyDictionary<long, long>> BlockTimestampsAsync(IReadOnlyCollection<long> blocks) =>
            Task.FromResult<IReadOnlyDictionary<long, long>>(blocks.ToDictionary(b => b, b => 1_700_000_000 + b * 1000));
        public Task<RawPosition?> PositionAsync(PositionRef p) => Task.FromResult(positions.GetValueOrDefault((int)p.TokenId));
        public Task<string?> OwnerOfAsync(PositionRef p) => Task.FromResult(owners.GetValueOrDefault((int)p.TokenId));
        public Task<string> PoolAsync(string m, string a, string b, int s) => Task.FromResult("0x4444444444444444444444444444444444444444");
        public Task<Slot0> Slot0Async(string pool) => Task.FromResult(new Slot0(SqrtX96(3500), TickFor(3500)));
        public Task<string> GaugeAsync(string pool) => Task.FromResult(Gauge);
        public Task<bool> StakedContainsAsync(string g, string o, BigInteger id) => Task.FromResult(staked);
        public Task<BigInteger> EarnedAeroAsync(string g, string o, BigInteger id) => Task.FromResult(50 * BigInteger.Pow(10, 18));
        public Task<(BigInteger, BigInteger)> PendingFeesAsync(string o, PositionRef p) =>
            Task.FromResult<(BigInteger, BigInteger)>((0, 12_000_000));
        public Task<TokenMeta> TokenAsync(string a) =>
            Task.FromResult(a.Equals(Weth, StringComparison.OrdinalIgnoreCase) ? new TokenMeta("WETH", 18) : new TokenMeta("USDC", 6));
    }

    private sealed class FakePrices : IPriceSource
    {
        public Task<IReadOnlyDictionary<string, decimal>> CurrentAsync(IEnumerable<string> a) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(
                new Dictionary<string, decimal> { [Weth] = 3500, [Usdc] = 1, [AerodromeAdapter.Aero] = 0.8m });
        public Task<IReadOnlyDictionary<string, decimal>> AtAsync(long ts, IEnumerable<string> a) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(
                new Dictionary<string, decimal> { [Weth] = ts < 1_700_000_000 + 2000 * 1000 ? 3000 : 3200, [Usdc] = 1 });
    }

    private static Task<WalletResult> Run(FakeReader reader) =>
        new AerodromeAdapter(reader, new FakePrices()).GetPositionsAsync(Owner);

    private static BigInteger E18(decimal v) => new(v * 1e18m);

    [Fact]
    public async Task UnstakedInRangePositionValueFeesAndPnlLineUp()
    {
        var reader = new FakeReader(new() { [1] = Owner }, new() { [1] = Raw(L) }, [
            Log(1, EventKind.Add, 1, E18(1), 3000_000_000),
            Log(1, EventKind.Remove, 3000, E18(0.1m), 300_000_000),
            Log(1, EventKind.Collect, 3000, E18(0.1m), 305_000_000),
        ]);
        var result = await Run(reader);
        Assert.Empty(result.Warnings);
        var p = Assert.Single(result.Positions);
        Assert.Equal(PositionState.In, p.State);
        Assert.False(p.Staked);
        Assert.Equal("WETH / USDC", p.Pair);
        Assert.InRange(p.PriceCurrent, 3499, 3501);
        Assert.True(p.PriceLower < p.PriceCurrent && p.PriceCurrent < p.PriceUpper);
        Assert.InRange(p.Tokens[0].Amount, 0.99, 1.01); // L was sized for ~1 ETH
        Assert.Equal(RewardKind.TradingFees, p.Rewards.Kind);
        Assert.Equal(12m, p.Rewards.UnclaimedUsd);
        Assert.Equal(6000m, p.Pnl.DepositedUsd);
        Assert.Equal(p.Pnl.NetUsd, p.Pnl.PriceEffectUsd + p.Pnl.IlUsd + p.Pnl.FeesUsd);
        Assert.Equal(p.ValueUsd + 12 + (0.1m * 3200 + 305) - 6000, p.Pnl.NetUsd);
        Assert.Equal(1, result.Summary.OpenCount);
        Assert.Contains(p.Incomplete, s => s.Contains("Gas"));
    }

    [Fact]
    public async Task StakedPositionEarnsAeroEmissionsNotTradingFees()
    {
        var reader = new FakeReader(new() { [2] = Gauge }, new() { [2] = Raw(L) },
            [Log(2, EventKind.Add, 1, E18(1), 3000_000_000)], staked: true);
        var p = Assert.Single((await Run(reader)).Positions);
        Assert.True(p.Staked);
        Assert.Equal(RewardKind.AeroEmissions, p.Rewards.Kind);
        Assert.Equal(40m, p.Rewards.UnclaimedUsd); // 50 AERO at $0.8
        Assert.Contains(p.Incomplete, s => s.Contains("claimed"));
    }

    [Fact]
    public async Task ForeignNftsAreLeftOutAndBurnedOnesWarn()
    {
        var reader = new FakeReader(new() { [3] = Other, [4] = null }, new() { [3] = Raw(L), [4] = null },
            [Log(3, EventKind.Add, 1, 1, 1)]);
        var result = await Run(reader);
        Assert.Empty(result.Positions);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("#4", warning);
    }

    [Fact]
    public async Task BurnedPositionRecoveredFromBeforeTheBurnIsClosedAndNeedsNoCustody()
    {
        var burned = Raw(0) with { Burned = true };
        var reader = new FakeReader(new() { [7] = null }, new() { [7] = burned }, [
            Log(7, EventKind.Add, 1, E18(1), 3000_000_000),
            Log(7, EventKind.Remove, 3000, E18(1), 3000_000_000),
            Log(7, EventKind.Collect, 3000, E18(1), 3050_000_000),
        ]);
        var result = await Run(reader);
        Assert.Empty(result.Warnings);
        var p = Assert.Single(result.Positions);
        Assert.Equal(PositionState.Closed, p.State);
        Assert.Equal(0m, p.ValueUsd);
        Assert.Equal(0m, p.Rewards.UnclaimedUsd);
        Assert.Equal(p.Pnl.NetUsd, p.Pnl.PriceEffectUsd + p.Pnl.IlUsd + p.Pnl.FeesUsd);
    }

    [Fact]
    public async Task StakedElsewhereIsNotCounted()
    {
        var reader = new FakeReader(new() { [6] = Gauge }, new() { [6] = Raw(L) }, [Log(6, EventKind.Add, 1, 1, 1)], staked: false);
        Assert.Empty((await Run(reader)).Positions);
    }

    [Fact]
    public async Task EmptyPositionStillHeldIsClosed()
    {
        var reader = new FakeReader(new() { [5] = Owner }, new() { [5] = Raw(0) }, [
            Log(5, EventKind.Add, 1, E18(1), 3000_000_000),
            Log(5, EventKind.Remove, 3000, E18(1), 3000_000_000),
            Log(5, EventKind.Collect, 3000, E18(1), 3000_000_000),
        ]);
        var p = Assert.Single((await Run(reader)).Positions);
        Assert.Equal(PositionState.Closed, p.State);
        Assert.NotNull(p.ClosedAt);
        Assert.Equal(0m, p.ValueUsd);
    }
}

public class DemoChainTests
{
    [Fact]
    public async Task DemoWalletProducesAllPositionKinds()
    {
        const string wallet = "0x7a3f000000000000000000000000000000c91e00";
        var demo = new DemoChain(wallet);
        var result = await new AerodromeAdapter(demo, demo).GetPositionsAsync(wallet, demo: true);

        Assert.True(result.Demo);
        Assert.Equal(5, result.Positions.Count);
        Assert.Single(result.Warnings); // the burned NFT
        Assert.Contains(result.Positions, p => p.State == PositionState.Out && p.Staked);
        Assert.Contains(result.Positions, p => p.State == PositionState.Closed);
        Assert.Contains(result.Positions, p => p.State == PositionState.In && !p.Staked);
        Assert.All(result.Positions, p =>
            Assert.True(Math.Abs(p.Pnl.NetUsd - (p.Pnl.PriceEffectUsd + p.Pnl.IlUsd + p.Pnl.FeesUsd)) < 0.01m, p.Pair));
        Assert.All(result.Positions.Where(p => p.State != PositionState.Closed), p => Assert.InRange(p.ValueUsd, 1000, 40000));
    }
}

public class AbiTests
{
    [Fact]
    public void SelectorsAndTopicsMatchKnownValues()
    {
        Assert.Equal("0xa9059cbb", Abi.Selector("transfer(address,uint256)"));
        Assert.Equal("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef", Abi.Topic("Transfer(address,address,uint256)"));
    }

    [Fact]
    public void WordsRoundTripIncludingNegativeInts()
    {
        Assert.Equal(64, Abi.Word(255).Length);
        Assert.Equal(255, (int)Abi.ToUInt(Abi.Word(255)));
        Assert.Equal(-100, (int)Abi.ToInt(new string('f', 62) + "9c"));
        Assert.Equal("0x4200000000000000000000000000000000000006", Abi.ToAddress(Abi.AddressWord("0x4200000000000000000000000000000000000006")));
    }

    [Fact]
    public void DecodesDynamicStringAndBytes32()
    {
        var dynamic = Abi.Word(32) + Abi.Word(4) + "5745544800000000000000000000000000000000000000000000000000000000";
        Assert.Equal("WETH", Abi.DecodeString(dynamic));
        Assert.Equal("MKR", Abi.DecodeString("0x4d4b520000000000000000000000000000000000000000000000000000000000"));
    }
}
