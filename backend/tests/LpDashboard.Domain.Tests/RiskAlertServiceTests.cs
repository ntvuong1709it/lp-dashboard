using System.Numerics;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Domain.Tests;

public sealed class RiskAlertServiceTests : IDisposable
{
    private const string Token = "0x1111111111111111111111111111111111111111";
    private readonly RiskHarness _h = new();

    public void Dispose() => _h.Dispose();

    /// <summary>Runs a real check (sources, evaluator, change detector, storage) one day after the previous one.</summary>
    private async Task<TokenCheckResult> CheckAsync(Action? change = null)
    {
        _h.Time.Advance(TimeSpan.FromDays(1));
        change?.Invoke();
        return await _h.Service<RiskService>().CheckTokenAsync(Chain.Base, Token, default);
    }

    private Task HandleAsync(TokenCheckResult result, bool held = true) =>
        _h.Service<RiskAlertService>().HandleTokenCheckAsync(Chain.Base, result, held, default);

    private void Honeypot(bool on) => _h.GoPlus.Reply = RiskHarness.GoPlusOk() with { IsHoneypot = on };

    private async Task<int> StateRowsAsync()
    {
        await using var db = _h.NewDb();
        return await db.RiskAlertStates.CountAsync();
    }

    [Fact]
    public async Task First_check_is_silent_even_when_it_is_red()
    {
        Honeypot(true);
        await HandleAsync(await CheckAsync());

        Assert.Empty(_h.Notifier.Messages);
        Assert.Equal(0, await StateRowsAsync());
    }

    [Fact]
    public async Task A_worsening_on_a_held_token_sends_one_message_with_before_and_after()
    {
        await HandleAsync(await CheckAsync());
        await HandleAsync(await CheckAsync(() => Honeypot(true)));

        var message = Assert.Single(_h.Notifier.Messages);
        Assert.Contains("Risk got worse", message);
        Assert.Contains("TKN", message);
        Assert.Contains("Red", message);
        Assert.Contains("Risk level Green -> Red", message);
        Assert.Contains("Honeypot or failing sell", message);
        Assert.Contains("basescan.org/address/" + Token, message);
    }

    [Fact]
    public async Task The_same_alert_is_not_repeated_while_the_problem_stays()
    {
        await HandleAsync(await CheckAsync());
        var worse = await CheckAsync(() => Honeypot(true));
        await HandleAsync(worse);
        await HandleAsync(worse); // the very same result handled twice
        await HandleAsync(await CheckAsync()); // still a honeypot a day later: no change, no alert

        Assert.Single(_h.Notifier.Messages);
    }

    [Fact]
    public async Task Alert_is_rearmed_once_the_problem_clears()
    {
        await HandleAsync(await CheckAsync());
        await HandleAsync(await CheckAsync(() => Honeypot(true)));
        await HandleAsync(await CheckAsync(() => Honeypot(false)));
        await HandleAsync(await CheckAsync(() => Honeypot(true)));

        Assert.Equal(2, _h.Notifier.Messages.Count);
        await using var db = _h.NewDb();
        Assert.Contains(await db.RiskAlertStates.ToListAsync(), s => s.ClearedAt is not null);
    }

    [Fact]
    public async Task A_failed_delivery_is_logged_and_never_repeated()
    {
        _h.Notifier.FailWith = "Telegram returned 400";
        await HandleAsync(await CheckAsync());
        var worse = await CheckAsync(() => Honeypot(true));

        await HandleAsync(worse);
        await HandleAsync(worse);

        Assert.Equal(1, _h.Notifier.Attempts);
        Assert.Empty(_h.Notifier.Messages);
    }

    [Fact]
    public async Task Tokens_that_are_only_starred_do_not_alert()
    {
        await HandleAsync(await CheckAsync(), held: false);
        await HandleAsync(await CheckAsync(() => Honeypot(true)), held: false);

        Assert.Empty(_h.Notifier.Messages);
        Assert.Equal(0, await StateRowsAsync());
    }

    [Fact]
    public async Task Without_a_configured_channel_nothing_is_sent_and_nothing_is_recorded()
    {
        _h.Notifier.Configured = false;
        await HandleAsync(await CheckAsync());
        await HandleAsync(await CheckAsync(() => Honeypot(true)));

        Assert.Equal(0, _h.Notifier.Attempts);
        Assert.Equal(0, await StateRowsAsync());
    }

    [Fact]
    public async Task A_controller_change_alerts_once_per_new_address()
    {
        const string Safe = "0x3333333333333333333333333333333333333333";
        const string Eoa = "0x4444444444444444444444444444444444444444";
        _h.GoPlus.Reply = RiskHarness.GoPlusOk() with { CanMint = true };
        _h.OnChain.ByToken[Token] = new OnChainFacts(Safe, true, null, null, null);
        await HandleAsync(await CheckAsync());

        var changed = await CheckAsync(() => _h.OnChain.ByToken[Token] = new OnChainFacts(Eoa, false, null, null, null));
        await HandleAsync(changed);
        await HandleAsync(changed);

        var message = Assert.Single(_h.Notifier.Messages);
        Assert.Contains("Controller changed", message);
        Assert.Contains(Eoa, message);
    }

    [Fact]
    public async Task Level_alert_fires_again_after_the_token_dropped_below_that_level()
    {
        await HandleAsync(await CheckAsync());
        await HandleAsync(await CheckAsync(() => Honeypot(true)));       // Green -> Red: alert 1
        await HandleAsync(await CheckAsync());                           // still Red a day later: no news
        await HandleAsync(await CheckAsync(() => Honeypot(false)));      // back to Green: clears
        await HandleAsync(await CheckAsync(() => Honeypot(true)));       // Red again: alert 2

        Assert.Equal(2, _h.Notifier.Messages.Count);
    }

    // ---- liquidity alert

    [Fact]
    public async Task Liquidity_alert_fires_once_and_is_rearmed_when_the_drop_is_gone()
    {
        var alerts = () => _h.Service<RiskAlertService>();
        var drop = new PoolLiquidityChange(40m, 1000m, 600m);
        var fine = new PoolLiquidityChange(5m, 1000m, 950m);

        await alerts().HandlePoolLiquidityAsync(Chain.Base, "0xPOOL", "AAA/BBB", drop, 30m, default);
        await alerts().HandlePoolLiquidityAsync(Chain.Base, "0xpool", "AAA/BBB", drop, 30m, default);
        Assert.Single(_h.Notifier.Messages);
        Assert.Contains("AAA/BBB", _h.Notifier.Messages[0]);
        Assert.Contains("$1,000 -> $600", _h.Notifier.Messages[0]);
        Assert.Contains("40", _h.Notifier.Messages[0]);

        await alerts().HandlePoolLiquidityAsync(Chain.Base, "0xpool", "AAA/BBB", fine, 30m, default);
        await alerts().HandlePoolLiquidityAsync(Chain.Base, "0xpool", "AAA/BBB", drop, 30m, default);
        Assert.Equal(2, _h.Notifier.Messages.Count);
    }

    [Fact]
    public async Task No_baseline_means_no_liquidity_alert_and_no_clearing()
    {
        await _h.Service<RiskAlertService>().HandlePoolLiquidityAsync(Chain.Base, "0xpool", "AAA/BBB", null, 30m, default);
        Assert.Empty(_h.Notifier.Messages);
    }
}

public class PoolLiquidityTests
{
    private static BigInteger Units(decimal amount, int decimals) => new(amount * (decimal)Math.Pow(10, decimals));

    private static PoolLiquidityChange? Compare(
        decimal then0, decimal then1, decimal now0, decimal now1, decimal price0 = 2m, decimal price1 = 1m) =>
        PoolLiquidityDropDetector.Compare(Units(then0, 18), Units(then1, 6), Units(now0, 18), Units(now1, 6), 18, 6, price0, price1);

    [Fact]
    public void A_price_move_alone_is_not_a_drop()
    {
        // The pool rebalanced between its tokens as the price moved: 100 AAA + 0 BBB became 50 AAA + 100 BBB.
        // Valued at today's prices ($2 and $1) that is the same $200.
        var c = Compare(100, 0, 50, 100)!;
        Assert.Equal(200m, c.ValueThenUsd);
        Assert.Equal(200m, c.ValueNowUsd);
        Assert.Equal(0m, c.DropPct);
        Assert.False(c.Exceeds(30m));
    }

    [Fact]
    public void A_thirty_one_percent_drop_in_raw_balances_is_a_drop()
    {
        var c = Compare(100, 100, 69, 69)!;
        Assert.Equal(31m, c.DropPct);
        Assert.True(c.Exceeds(30m));
    }

    [Fact]
    public void Exactly_the_threshold_is_not_above_it() =>
        Assert.False(Compare(100, 100, 70, 70)!.Exceeds(30m));

    [Fact]
    public void Growth_is_a_negative_drop()
    {
        var c = Compare(100, 100, 150, 150)!;
        Assert.Equal(-50m, c.DropPct);
        Assert.False(c.Exceeds(30m));
    }

    [Fact]
    public void An_empty_baseline_has_nothing_to_compare() =>
        Assert.Null(Compare(0, 0, 100, 100));

    [Fact]
    public void Both_tokens_are_valued_at_the_prices_given()
    {
        // Same raw amounts, but valued at different prices than "then" would imply: only raw balances differ between the two sides.
        var c = Compare(10, 1000, 10, 0, price0: 100m, price1: 1m)!;
        Assert.Equal(2000m, c.ValueThenUsd);
        Assert.Equal(1000m, c.ValueNowUsd);
        Assert.Equal(50m, c.DropPct);
    }
}
