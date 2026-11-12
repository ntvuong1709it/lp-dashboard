using LpDashboard.Domain.Risk;

namespace LpDashboard.Domain.Tests;

public class RiskChangeDetectorTests
{
    private static readonly RiskOptions Options = new();

    private static TokenRiskAssessment Eval(TokenRiskFacts f) => RiskEvaluator.EvaluateToken(f, Options);

    private static TokenRiskFacts Clean() => RiskTestData.Clean();

    private static RiskComparison Compare(TokenRiskFacts? before, TokenRiskFacts after) =>
        RiskChangeDetector.Compare(before is null ? null : Eval(before), Eval(after));

    [Fact]
    public void First_check_never_alerts_even_when_red()
    {
        var c = Compare(null, Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) });
        Assert.True(c.IsFirstCheck);
        Assert.False(c.ShouldAlert);
        Assert.Empty(c.Changes);
    }

    [Fact]
    public void Identical_checks_report_nothing()
    {
        var c = Compare(Clean(), Clean());
        Assert.False(c.IsFirstCheck);
        Assert.Empty(c.Changes);
        Assert.False(c.ShouldAlert);
    }

    [Fact]
    public void Honeypot_appearing_alerts_and_the_level_worsens()
    {
        var c = Compare(Clean(), Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) });
        Assert.True(c.ShouldAlert);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.Appeared && x.Code == RiskCodes.Honeypot && x.AlertWorthy && x.IsWorse);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.LevelWorsened && x.Description.Contains("Green -> Red"));
    }

    [Fact]
    public void Level_worsening_alerts_even_for_a_finding_that_is_not_special()
    {
        var c = Compare(Clean(), Clean() with { Market = RiskTestData.Market(liquidity: 1000) });
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.Appeared && x.Code == RiskCodes.ThinLiquidity && !x.AlertWorthy);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.LevelWorsened && x.AlertWorthy);
        Assert.True(c.ShouldAlert);
    }

    [Fact]
    public void A_second_yellow_finding_on_a_yellow_token_does_not_alert()
    {
        var yellow = Clean() with { Market = RiskTestData.Market(liquidity: 1000) };
        var moreYellow = yellow with { GoPlus = RiskTestData.GoPlus(openSource: false) };
        var c = Compare(yellow, moreYellow);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.Appeared && x.Code == RiskCodes.UnverifiedSource);
        Assert.False(c.ShouldAlert);
    }

    [Fact]
    public void Clearing_a_finding_is_reported_without_alert()
    {
        var c = Compare(Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) }, Clean());
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.Cleared && x.Code == RiskCodes.Honeypot && !x.IsWorse);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.LevelImproved);
        Assert.False(c.ShouldAlert);
    }

    [Fact]
    public void A_finding_is_not_cleared_when_its_sources_did_not_answer()
    {
        var before = Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) };
        // GoPlus and honeypot.is both down this time: the honeypot finding is simply unknown, not gone.
        var after = Clean() with
        {
            Sources = [RiskTestData.Down(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.HoneypotIs, true), RiskTestData.Ok(RiskSources.Rpc, true), RiskTestData.Ok(RiskSources.DexScreener)],
            GoPlus = null, HoneypotIs = null,
        };
        var c = Compare(before, after);
        Assert.DoesNotContain(c.Changes, x => x.Kind == RiskChangeKind.Cleared);
        Assert.False(c.ShouldAlert);
    }

    [Fact]
    public void A_finding_is_cleared_when_any_one_of_its_sources_answers()
    {
        var before = Clean() with { HoneypotIs = RiskTestData.HoneypotIs(honeypot: true) };
        var after = Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.HoneypotIs, true), RiskTestData.Ok(RiskSources.Rpc, true)],
            HoneypotIs = null,
        };
        Assert.Contains(Compare(before, after).Changes, x => x.Kind == RiskChangeKind.Cleared && x.Code == RiskCodes.Honeypot);
    }

    [Fact]
    public void Severity_raised_on_a_watched_code_alerts()
    {
        // Mint power controlled by a contract (yellow), then the owner becomes an EOA (red).
        var yellow = Clean() with { GoPlus = RiskTestData.GoPlus(mint: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.SafeOwner, ownerIsContract: true) };
        var red = Clean() with { GoPlus = RiskTestData.GoPlus(mint: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.SafeOwner, ownerIsContract: false) };
        var c = Compare(yellow, red);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.SeverityRaised && x.Code == RiskCodes.PrivilegedMintOrBlacklist && x.AlertWorthy);
    }

    [Fact]
    public void Privileged_proxy_and_concentration_findings_alert_when_they_appear()
    {
        var mint = Compare(Clean(), Clean() with { GoPlus = RiskTestData.GoPlus(mint: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.SafeOwner, ownerIsContract: true) });
        Assert.Contains(mint.Changes, x => x.Code == RiskCodes.PrivilegedMintOrBlacklist && x.AlertWorthy);

        var proxy = Compare(Clean(), Clean() with { OnChain = RiskTestData.OnChain(impl: RiskTestData.Impl, admin: RiskTestData.Admin, adminIsContract: true) });
        Assert.Contains(proxy.Changes, x => x.Code == RiskCodes.UpgradeableProxy && x.Kind == RiskChangeKind.Appeared && x.AlertWorthy);
        Assert.True(proxy.ShouldAlert);

        var whale = Compare(Clean(), Clean() with { GoPlus = RiskTestData.GoPlus(holders: [RiskTestData.Holder(RiskTestData.Whale, 55)]) });
        Assert.Contains(whale.Changes, x => x.Code == RiskCodes.TopHolderConcentration && x.AlertWorthy);
    }

    [Fact]
    public void Controller_change_alerts()
    {
        var before = Clean() with { GoPlus = RiskTestData.GoPlus(blacklist: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.SafeOwner, ownerIsContract: true) };
        var after = Clean() with { GoPlus = RiskTestData.GoPlus(blacklist: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: true) };
        var c = Compare(before, after);
        var change = Assert.Single(c.Changes, x => x.Kind == RiskChangeKind.ControllerChanged);
        Assert.True(change.AlertWorthy);
        Assert.Contains(RiskTestData.SafeOwner, change.Description);
        Assert.Contains(RiskTestData.EoaOwner, change.Description);
    }

    [Fact]
    public void Ownership_returning_after_a_renounce_alerts_but_renouncing_does_not()
    {
        var renounced = Clean();
        var owned = Clean() with { OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false) };
        Assert.Contains(Compare(renounced, owned).Changes, x => x.Kind == RiskChangeKind.ControllerChanged && x.AlertWorthy);
        Assert.DoesNotContain(Compare(owned, renounced).Changes, x => x.Kind == RiskChangeKind.ControllerChanged);
    }

    [Fact]
    public void Controller_change_needs_both_checks_to_have_read_the_chain()
    {
        var before = Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Ok(RiskSources.HoneypotIs, true), RiskTestData.Down(RiskSources.Rpc, true)],
            OnChain = null,
        };
        var after = Clean() with { OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false) };
        Assert.DoesNotContain(Compare(before, after).Changes, x => x.Kind == RiskChangeKind.ControllerChanged);
    }

    [Fact]
    public void Implementation_change_alerts()
    {
        var before = Clean() with { OnChain = RiskTestData.OnChain(impl: RiskTestData.Impl, admin: RiskTestData.Admin, adminIsContract: true) };
        var after = Clean() with { OnChain = RiskTestData.OnChain(impl: RiskTestData.Addr(42), admin: RiskTestData.Admin, adminIsContract: true) };
        var c = Compare(before, after);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.ImplementationChanged && x.AlertWorthy);
        Assert.Contains(RiskTestData.Addr(42), c.Changes.Single(x => x.Kind == RiskChangeKind.ImplementationChanged).Description);
    }

    [Theory]
    [InlineData(10, 20, false)] // rose by exactly 10 points: not more than 10
    [InlineData(10, 21, true)]
    [InlineData(5, 29, true)]
    public void Top_holder_share_rising_by_more_than_ten_points_alerts(double was, double now, bool alert)
    {
        var before = Clean() with { GoPlus = RiskTestData.GoPlus(holders: [RiskTestData.Holder(RiskTestData.Whale, (decimal)was)]) };
        var after = Clean() with { GoPlus = RiskTestData.GoPlus(holders: [RiskTestData.Holder(RiskTestData.Whale, (decimal)now)]) };
        var c = Compare(before, after);
        Assert.Equal(alert, c.Changes.Any(x => x.Kind == RiskChangeKind.TopHolderShareRose && x.AlertWorthy));
        Assert.Equal(alert, c.ShouldAlert);
    }

    [Fact]
    public void Inconclusive_baseline_does_not_alert_for_yellow_but_does_for_red()
    {
        var partial = Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.HoneypotIs, true), RiskTestData.Ok(RiskSources.Rpc, true)],
            HoneypotIs = null,
        };
        Assert.Equal(RiskLevel.Partial, Eval(partial).Verdict.Level);

        var nowYellow = Compare(partial, Clean() with { GoPlus = RiskTestData.GoPlus(openSource: false) });
        Assert.DoesNotContain(nowYellow.Changes, x => x.Kind == RiskChangeKind.LevelWorsened);
        Assert.False(nowYellow.ShouldAlert);

        var nowRed = Compare(partial, Clean() with { HoneypotIs = RiskTestData.HoneypotIs(honeypot: true) });
        Assert.True(nowRed.ShouldAlert);
    }

    [Fact]
    public void Bookkeeping_findings_are_never_changes()
    {
        var down = Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Ok(RiskSources.HoneypotIs, true), RiskTestData.Ok(RiskSources.Rpc, true), RiskTestData.Down(RiskSources.DexScreener)],
            Market = null,
        };
        var c = Compare(Clean(), down);
        Assert.Empty(c.Changes);
    }

    [Fact]
    public void Pool_level_and_finding_changes_are_reported()
    {
        var green = Eval(Clean());
        PoolRiskAssessment Pool(DateTime? created, LpConcentrationFacts? lp = null) => RiskEvaluator.EvaluatePool(
            new PoolRiskFacts(RiskTestData.PoolAddr, RiskTestData.Now, [RiskTestData.Ok(RiskSources.DexScreener, true), RiskTestData.Ok(RiskSources.LpIndex)], created, 10, 0, null, lp),
            green, green, Options);

        Assert.True(RiskChangeDetector.ComparePool(null, Pool(RiskTestData.Now.AddDays(-50))).IsFirstCheck);

        var c = RiskChangeDetector.ComparePool(Pool(RiskTestData.Now.AddDays(-50)), Pool(RiskTestData.Now.AddDays(-50), new LpConcentrationFacts(RiskTestData.Whale, 60, false)));
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.Appeared && x.Code == RiskCodes.LpConcentration);
        Assert.Contains(c.Changes, x => x.Kind == RiskChangeKind.LevelWorsened);
    }
}
