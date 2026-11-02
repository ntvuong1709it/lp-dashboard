using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using LpLedger.Core;
using LpLedger.Core.Models;
using static LpLedger.Core.ConcentratedLiquidityMath;

namespace LpLedger.Infrastructure.Demo;

/// <summary>
/// A simulated Base chain with a handful of Aerodrome positions, used when no RPC is configured so the
/// dashboard can be checked without a key. The real adapter runs on top of it, so everything except the
/// RPC reads is exercised. Prices follow smooth fake curves and are not market data.
/// </summary>
public sealed class DemoChain : IChainReader, IPriceSource
{
    private const string Weth = "0x4200000000000000000000000000000000000006";
    private const string Usdc = "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";
    private const string Aero = "0x940181a94a35a4569e4529a3cdfb74e38fd98631";
    private const string CbBtc = "0xcbb7c0000ab88b473b1f5afd9ef808440eed33bf";
    private const string Gauge = "0x1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f";
    private const string Manager = "0x4857db576ee0e45f10525ec59527bfd10beec60b";
    private const string Foreign = "0x2222222222222222222222222222222222222222";
    private const long BaseTs = 1_720_000_000, BaseBlock = 20_000_000;

    private static readonly Dictionary<string, (string Symbol, int Decimals, decimal Now, double Amp, double Period)> Tokens = new()
    {
        [Weth] = ("WETH", 18, 3450m, 0.12, 35),
        [Usdc] = ("USDC", 6, 1m, 0, 1),
        [Aero] = ("AERO", 18, 0.82m, 0.35, 28),
        [CbBtc] = ("cbBTC", 8, 106000m, 0.09, 45),
    };

    // Range is a multiple of today's pool price. Unstaked positions earn fees, staked ones AERO.
    private sealed record Scenario(
        int Id, string T0, string T1, int TickSpacing, double Low, double High,
        int OpenedDaysAgo, int? ClosedDaysAgo, decimal DepositUsd, bool Staked, double UnclaimedPct);

    private static readonly Scenario[] Scenarios =
    [
        new(1001, Weth, Usdc, 100, 0.87, 1.13, 74, null, 18000, false, 0.011),
        new(1002, Weth, CbBtc, 100, 0.92, 1.10, 41, null, 12000, true, 0.014),
        new(1003, Usdc, Aero, 200, 0.55, 0.90, 33, null, 4200, true, 0.021),
        new(1004, Weth, Usdc, 100, 0.93, 1.006, 20, null, 5000, false, 0.006),
        new(1005, Weth, Usdc, 100, 0.85, 1.20, 120, 30, 5000, false, 0),
    ];

    private readonly string _owner;
    private readonly long _now;
    private readonly Dictionary<BigInteger, (RawPosition? Pos, string? Holder)> _positions = [];
    private readonly Dictionary<BigInteger, (BigInteger A0, BigInteger A1)> _pending = [];
    private readonly Dictionary<BigInteger, BigInteger> _earned = [];
    private readonly Dictionary<string, Slot0> _slots = [];
    private readonly List<RawLiquidityLog> _logs = [];

    public DemoChain(string owner)
    {
        _owner = owner;
        _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 2 * 2;
        foreach (var s in Scenarios) Build(s);
        _positions[1006] = (null, null);          // burned NFT: produces a warning
        _positions[1007] = (_positions[1001].Pos, Foreign); // sent to someone else: left out silently
    }

    private double Price(string token, double daysAgo)
    {
        var t = Tokens[token];
        return (double)t.Now * (1 + t.Amp * Math.Sin(daysAgo / t.Period));
    }

    private double PoolPrice(Scenario s, double daysAgo) => Price(s.T0, daysAgo) / Price(s.T1, daysAgo);

    private static double RawPrice(double humanPrice, int dec0, int dec1) => humanPrice / Math.Pow(10, dec0 - dec1);

    private static int AlignedTick(double rawPrice, int spacing, bool up)
    {
        var steps = Math.Log(rawPrice) / Math.Log(1.0001) / spacing;
        return (int)(up ? Math.Ceiling(steps) : Math.Floor(steps)) * spacing;
    }

    private static BigInteger Units(double amount, int decimals) => new(amount * Math.Pow(10, decimals));

    private string Hash(params object[] parts) =>
        "0x" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();

    private string PoolAddress(string t0, string t1, int spacing) => Hash(t0, t1, spacing)[..42];

    private long BlockAt(long ts) => BaseBlock + (ts - BaseTs) / 2;

    private void Build(Scenario s)
    {
        var d0 = Tokens[s.T0].Decimals;
        var d1 = Tokens[s.T1].Decimals;
        var nowPrice = PoolPrice(s, 0);
        var tickLower = AlignedTick(RawPrice(nowPrice * s.Low, d0, d1), s.TickSpacing, false);
        var tickUpper = AlignedTick(RawPrice(nowPrice * s.High, d0, d1), s.TickSpacing, true);

        // Size liquidity so the deposit at the entry price is worth DepositUsd.
        double Sqrt(double human) => Math.Sqrt(RawPrice(human, d0, d1));
        var entry = PoolPrice(s, s.OpenedDaysAgo);
        var (u0, u1) = AmountsForLiquidity(BigInteger.Pow(10, 18), Sqrt(entry), tickLower, tickUpper, d0, d1);
        var perL = u0 * Price(s.T0, s.OpenedDaysAgo) + u1 * Price(s.T1, s.OpenedDaysAgo);
        var liquidity = new BigInteger((double)s.DepositUsd / perL * 1e18);

        long Ts(int daysAgo) => _now - (long)daysAgo * 86400;
        int logIndex = 0;
        void Log(EventKind kind, int daysAgo, double a0, double a1)
        {
            var ts = Ts(daysAgo);
            _logs.Add(new RawLiquidityLog(kind, new PositionRef(Manager, s.Id), Units(a0, d0), Units(a1, d1), BlockAt(ts),
                Hash(s.Id, kind, daysAgo), logIndex++));
        }

        var (add0, add1) = AmountsForLiquidity(liquidity, Sqrt(entry), tickLower, tickUpper, d0, d1);
        Log(EventKind.Add, s.OpenedDaysAgo, add0, add1);

        var end = s.ClosedDaysAgo ?? 0;
        if (!s.Staked)
        {
            // A fee collection roughly every 18 days, each worth about 0.9% of the deposit, split by value.
            for (var d = s.OpenedDaysAgo - 18; d > end; d -= 18)
            {
                var half = (double)s.DepositUsd * 0.009 / 2;
                Log(EventKind.Collect, d, half / Price(s.T0, d), half / Price(s.T1, d));
            }
        }

        if (s.ClosedDaysAgo is { } closed)
        {
            var (r0, r1) = AmountsForLiquidity(liquidity, Sqrt(PoolPrice(s, closed)), tickLower, tickUpper, d0, d1);
            Log(EventKind.Remove, closed, r0, r1);
            var half = (double)s.DepositUsd * 0.006 / 2;
            Log(EventKind.Collect, closed, r0 + half / Price(s.T0, closed), r1 + half / Price(s.T1, closed));
            liquidity = 0;
        }

        _positions[s.Id] = (new RawPosition(s.T0, s.T1, s.TickSpacing, tickLower, tickUpper, liquidity),
            s.Staked ? Gauge : _owner);

        var pool = PoolAddress(s.T0, s.T1, s.TickSpacing);
        var raw = RawPrice(nowPrice, d0, d1);
        _slots[pool] = new Slot0(new BigInteger(Math.Sqrt(raw) * Math.Pow(2, 96)), (int)Math.Floor(Math.Log(raw) / Math.Log(1.0001)));

        var unclaimedUsd = (double)s.DepositUsd * s.UnclaimedPct;
        if (s.Staked) _earned[s.Id] = Units(unclaimedUsd / (double)Tokens[Aero].Now, 18);
        else _pending[s.Id] = (Units(unclaimedUsd / 2 / Price(s.T0, 0), d0), Units(unclaimedUsd / 2 / Price(s.T1, 0), d1));
    }

    // IChainReader

    public Task<IReadOnlyList<PositionRef>> CandidatesAsync(string owner) =>
        Task.FromResult<IReadOnlyList<PositionRef>>(_positions.Keys.OrderBy(k => k).Select(k => new PositionRef(Manager, k)).ToList());

    public Task<IReadOnlyList<RawLiquidityLog>> LiquidityLogsAsync(string owner, IReadOnlyList<PositionRef> refs) =>
        Task.FromResult<IReadOnlyList<RawLiquidityLog>>(_logs.Where(l => refs.Any(r => r.Key == l.Ref.Key)).ToList());

    public Task<IReadOnlyDictionary<long, long>> BlockTimestampsAsync(IReadOnlyCollection<long> blocks) =>
        Task.FromResult<IReadOnlyDictionary<long, long>>(blocks.ToDictionary(b => b, b => BaseTs + (b - BaseBlock) * 2));

    public Task<RawPosition?> PositionAsync(PositionRef position) => Task.FromResult(_positions[position.TokenId].Pos);

    public Task<string?> OwnerOfAsync(PositionRef position) => Task.FromResult(_positions[position.TokenId].Holder);

    public Task<string> PoolAsync(string manager, string token0, string token1, int tickSpacing) =>
        Task.FromResult(PoolAddress(token0, token1, tickSpacing));

    public Task<Slot0> Slot0Async(string pool) => Task.FromResult(_slots[pool]);

    public Task<string> GaugeAsync(string pool) => Task.FromResult(Gauge);

    public Task<bool> StakedContainsAsync(string gauge, string owner, BigInteger tokenId) => Task.FromResult(true);

    public Task<BigInteger> EarnedAeroAsync(string gauge, string owner, BigInteger tokenId) =>
        Task.FromResult(_earned.GetValueOrDefault(tokenId));

    public Task<(BigInteger Amount0, BigInteger Amount1)> PendingFeesAsync(string owner, PositionRef position) =>
        Task.FromResult(_pending.GetValueOrDefault(position.TokenId));

    public Task<TokenMeta> TokenAsync(string address)
    {
        var t = Tokens[address.ToLowerInvariant()];
        return Task.FromResult(new TokenMeta(t.Symbol, t.Decimals));
    }

    // IPriceSource

    public Task<IReadOnlyDictionary<string, decimal>> CurrentAsync(IEnumerable<string> addresses) =>
        AtAsync(_now, addresses);

    public Task<IReadOnlyDictionary<string, decimal>> AtAsync(long timestamp, IEnumerable<string> addresses)
    {
        var daysAgo = (_now - timestamp) / 86400.0;
        IReadOnlyDictionary<string, decimal> map = addresses
            .Select(a => a.ToLowerInvariant()).Distinct()
            .ToDictionary(a => a, a => (decimal)Price(a, daysAgo));
        return Task.FromResult(map);
    }
}
