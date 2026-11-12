using System.Numerics;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Domain.Tests;

/// <summary>The nightly re-check and the hourly liquidity snapshots, end to end over a scratch database with fake sources and chain.</summary>
public sealed class RiskMonitorTests : IDisposable
{
    private const string Owner = "0xabc0000000000000000000000000000000000001";
    private const string Pool = "0x00000000000000000000000000000000000000a1";
    private const string Aaa = "0x7777777777777777777777777777777777777771";
    private const string Bbb = "0x7777777777777777777777777777777777777772";

    private readonly RiskHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Task<RiskNightlySummary> NightlyAsync(bool onlyStale = false) =>
        _h.Service<RiskMonitorService>().RunNightlyAsync(onlyStale, default);

    private Task<LiquiditySummary> LiquidityAsync() => _h.Service<RiskMonitorService>().RunLiquidityAsync(default);

    private static BigInteger Units(decimal amount, int decimals) => new(amount * (decimal)Math.Pow(10, decimals));

    // ---- nightly

    [Fact]
    public async Task First_night_checks_every_held_token_and_stays_silent()
    {
        await _h.AddHeldPositionAsync(Owner, Pool, Aaa, Bbb);

        var summary = await NightlyAsync();

        Assert.Equal(2, summary.TokensChecked);
        Assert.Equal(2, _h.GoPlus.Calls);
        Assert.Empty(_h.Notifier.Messages);
        await using var db = _h.NewDb();
        Assert.Equal(2, await db.RiskChecks.CountAsync(c => c.IsLatest));
    }

    [Fact]
    public async Task A_synthetic_change_overnight_alerts_for_a_held_token_only_once()
    {
        await _h.AddHeldPositionAsync(Owner, Pool, Aaa, Bbb);
        await NightlyAsync();

        // Forced re-check the next night finds a honeypot flag on AAA.
        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.GoPlus.ByToken[Aaa] = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        var second = await NightlyAsync();
        Assert.Equal(2, second.TokensChecked);

        var message = Assert.Single(_h.Notifier.Messages);
        Assert.Contains("Risk got worse", message);
        Assert.Contains("Honeypot", message);

        // Same flag the night after: no news, no second alert.
        _h.Time.Advance(TimeSpan.FromDays(1));
        await NightlyAsync();
        Assert.Single(_h.Notifier.Messages);
    }

    [Fact]
    public async Task Catch_up_run_skips_tokens_that_are_still_fresh()
    {
        await _h.AddHeldPositionAsync(Owner, Pool, Aaa, Bbb);
        await NightlyAsync();
        var callsAfterFirst = _h.GoPlus.Calls;

        _h.Time.Advance(TimeSpan.FromHours(10));
        var fresh = await NightlyAsync(onlyStale: true);
        Assert.Equal(0, fresh.TokensChecked);
        Assert.Equal(2, fresh.TokensSkipped);
        Assert.Equal(callsAfterFirst, _h.GoPlus.Calls);

        _h.Time.Advance(TimeSpan.FromHours(15));
        var stale = await NightlyAsync(onlyStale: true);
        Assert.Equal(2, stale.TokensChecked);
    }

    [Fact]
    public async Task Starred_only_tokens_are_checked_but_never_alert()
    {
        await using (var db = _h.NewDb())
        {
            db.StarredPools.Add(new StarredPool { Chain = Chain.Base, ExplorerId = "llama-9", Symbol = "AAA-BBB", Token0 = Aaa, Token1 = Bbb, CreatedAt = RiskHarness.T0 });
            await db.SaveChangesAsync();
        }
        await NightlyAsync();
        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.GoPlus.ByToken[Aaa] = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        await NightlyAsync();

        Assert.Empty(_h.Notifier.Messages);
        await using var verify = _h.NewDb();
        Assert.Contains(await verify.RiskChecks.Where(c => c.Subject == Aaa).ToListAsync(), c => c.ChangesJson.Contains("Honeypot") || c.ChangesJson.Contains("HONEYPOT"));
    }

    [Fact]
    public async Task Nothing_to_check_is_a_quiet_night()
    {
        var summary = await NightlyAsync();
        Assert.Equal(new RiskNightlySummary(0, 0, 0, 0), summary);
        Assert.Equal(0, _h.GoPlus.Calls);
    }

    // ---- liquidity

    private async Task SetupPoolAsync(decimal aaa, decimal bbb)
    {
        await _h.AddHeldPositionAsync(Owner, Pool, Aaa, Bbb);
        _h.Prices.ByKey[Aaa] = 2m;
        _h.Prices.ByKey[Bbb] = 1m;
        SetBalances(aaa, bbb);
    }

    private void SetBalances(decimal aaa, decimal bbb)
    {
        _h.Provider.Set(Pool, Aaa, Units(aaa, 18));
        _h.Provider.Set(Pool, Bbb, Units(bbb, 6));
    }

    [Fact]
    public async Task First_snapshot_is_written_without_an_alert()
    {
        await SetupPoolAsync(100, 100);

        var summary = await LiquidityAsync();

        Assert.Equal(1, summary.PoolsSnapshotted);
        Assert.Empty(_h.Notifier.Messages);
        await using var db = _h.NewDb();
        var snapshot = await db.PoolLiquiditySnapshots.SingleAsync();
        Assert.Equal(Units(100, 18), snapshot.Token0Raw);
        Assert.Equal(Pool, snapshot.PoolAddress);
    }

    [Fact]
    public async Task A_31_percent_raw_drop_over_a_day_alerts_once()
    {
        await SetupPoolAsync(100, 100);
        await LiquidityAsync();

        _h.Time.Advance(TimeSpan.FromHours(24));
        SetBalances(69, 69);
        var summary = await LiquidityAsync();
        Assert.Equal(1, summary.DropsAlerted);
        var message = Assert.Single(_h.Notifier.Messages);
        Assert.Contains("Pool liquidity dropped: AAA/BBB", message);

        // An hour later it is still down against the day-old baseline: no second alert.
        _h.Time.Advance(TimeSpan.FromHours(1));
        await LiquidityAsync();
        Assert.Single(_h.Notifier.Messages);
    }

    [Fact]
    public async Task A_price_move_alone_does_not_alert()
    {
        await SetupPoolAsync(100, 0);
        await LiquidityAsync();

        // The pool sold half its AAA for BBB as the price moved: 50 AAA + 100 BBB is worth the same $200 at today's prices.
        _h.Time.Advance(TimeSpan.FromHours(24));
        SetBalances(50, 100);
        var summary = await LiquidityAsync();

        Assert.Equal(0, summary.DropsAlerted);
        Assert.Empty(_h.Notifier.Messages);
    }

    [Fact]
    public async Task Drop_of_30_percent_exactly_does_not_alert()
    {
        await SetupPoolAsync(100, 100);
        await LiquidityAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        SetBalances(70, 70);
        await LiquidityAsync();
        Assert.Empty(_h.Notifier.Messages);
    }

    [Fact]
    public async Task Pools_the_chain_cannot_read_are_counted_not_failed()
    {
        await _h.AddHeldPositionAsync(Owner, Pool, Aaa, Bbb); // no balances set: the node answered null
        var summary = await LiquidityAsync();
        Assert.Equal(0, summary.PoolsSnapshotted);
        Assert.Equal(1, summary.PoolsUnread);
    }

    [Fact]
    public async Task Old_snapshots_are_pruned()
    {
        await SetupPoolAsync(100, 100);
        await LiquidityAsync();
        _h.Time.Advance(TimeSpan.FromDays(31));
        await LiquidityAsync();

        await using var db = _h.NewDb();
        var snapshots = await db.PoolLiquiditySnapshots.ToListAsync();
        Assert.Single(snapshots);
        Assert.Equal(RiskHarness.T0.AddDays(31), snapshots[0].At);
    }
}
