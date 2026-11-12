using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Domain.Tests;

/// <summary>The real RiskService, collector and repository over a throwaway SQLite file, with fake sources: nothing touches the network.</summary>
public sealed class RiskServiceTests : IDisposable
{
    private const string Token = "0x1111111111111111111111111111111111111111";
    private const string Other = "0x2222222222222222222222222222222222222222";
    private static readonly DateTime T0 = RiskHarness.T0;

    private readonly RiskHarness _h = new();

    public void Dispose() => _h.Dispose();

    private RiskService Service() => _h.Service<RiskService>();

    private Task<TokenRiskDto> GetAsync(string address, bool refresh = false) =>
        Service().GetTokenAsync(Chain.Base, address, refresh, default);

    private async Task<RiskCheck> LatestRowAsync()
    {
        await using var db = _h.NewDb();
        return await db.RiskChecks.AsNoTracking().SingleAsync(c => c.IsLatest);
    }

    [Fact]
    public async Task First_lookup_runs_every_source_and_stores_a_green_check()
    {
        var dto = await GetAsync(Token);

        Assert.Equal(RiskLevel.Green, dto.Level);
        Assert.Equal(T0, dto.CheckedAt);
        Assert.Equal(5, dto.ChecksTotal);
        Assert.All(new[] { _h.GoPlus.Calls, _h.Honeypot.Calls, _h.OnChain.TokenCalls, _h.Market.TokenCalls }, c => Assert.Equal(1, c));
        Assert.Contains(dto.Sources, s => s.Name == RiskSources.GoPlus && s.Required && s.Answered);
        Assert.NotNull(dto.Links.Explorer);

        await using var db = _h.NewDb();
        var row = await db.RiskChecks.SingleAsync();
        Assert.True(row.IsLatest);
        Assert.Equal(RiskLevel.Green, row.Level);
        Assert.Equal(Token, row.Subject);
        Assert.Contains("goplus", row.SourcesJson);
    }

    [Fact]
    public async Task Cache_hit_calls_no_source()
    {
        await GetAsync(Token);
        _h.Time.Advance(TimeSpan.FromHours(23));
        var dto = await GetAsync(Token);

        Assert.Equal(T0, dto.CheckedAt);
        Assert.Equal(1, _h.GoPlus.Calls);
        Assert.Equal(1, _h.Honeypot.Calls);
        Assert.Equal(1, _h.Market.TokenCalls);
    }

    [Fact]
    public async Task Check_older_than_the_cache_window_is_run_again()
    {
        await GetAsync(Token);
        _h.Time.Advance(TimeSpan.FromHours(25));
        var dto = await GetAsync(Token);

        Assert.Equal(T0.AddHours(25), dto.CheckedAt);
        Assert.Equal(2, _h.GoPlus.Calls);
    }

    [Fact]
    public async Task Forced_refresh_inside_the_cooldown_is_refused_with_the_time_left()
    {
        await GetAsync(Token);
        _h.Time.Advance(TimeSpan.FromMinutes(2));

        var ex = await Assert.ThrowsAsync<RiskCooldownException>(() => GetAsync(Token, refresh: true));
        Assert.Equal(TimeSpan.FromMinutes(3), ex.RetryAfter);
        Assert.Equal(1, _h.GoPlus.Calls);
    }

    [Fact]
    public async Task Forced_refresh_after_the_cooldown_checks_again_and_the_dto_says_when_the_next_is_allowed()
    {
        var first = await GetAsync(Token);
        Assert.Equal(T0.AddMinutes(5), first.RecheckAvailableAt);

        _h.Time.Advance(TimeSpan.FromMinutes(6));
        var cached = await GetAsync(Token);
        Assert.Null(cached.RecheckAvailableAt);

        var dto = await GetAsync(Token, refresh: true);
        Assert.Equal(T0.AddMinutes(6), dto.CheckedAt);
        Assert.Equal(2, _h.GoPlus.Calls);
    }

    [Fact]
    public async Task Sources_only_ever_receive_the_token_address()
    {
        await GetAsync(Token.ToUpperInvariant().Replace("0X", "0x"));

        Assert.Equal([Token], _h.GoPlus.Tokens);
        Assert.Equal([Token], _h.Honeypot.Tokens);
        Assert.Equal([Token], _h.OnChain.Tokens);
        Assert.Equal([Token], _h.Market.Tokens);
    }

    [Fact]
    public async Task Source_timeout_degrades_to_partial_never_green()
    {
        _h.Honeypot.Hang = true;

        var dto = await GetAsync(Token);

        Assert.Equal(RiskLevel.Partial, dto.Level);
        Assert.Equal(4, dto.ChecksRun);
        Assert.Equal(5, dto.ChecksTotal);
        var failed = Assert.Single(dto.Sources, s => !s.Answered);
        Assert.Equal(RiskSources.HoneypotIs, failed.Name);
        Assert.Equal("timeout", failed.Detail);
        Assert.Contains(dto.Findings, f => f.Code == RiskCodes.SourceUnavailable);
    }

    [Fact]
    public async Task Source_that_throws_degrades_the_same_way()
    {
        _h.GoPlus.Throw = true;
        var dto = await GetAsync(Token);
        Assert.Equal(RiskLevel.Partial, dto.Level);
        Assert.Contains(dto.Sources, s => s.Name == RiskSources.GoPlus && !s.Answered);
    }

    [Fact]
    public async Task Address_with_no_contract_is_rejected_and_not_stored()
    {
        _h.OnChain.NoContract = true;

        await Assert.ThrowsAsync<RiskInputException>(() => GetAsync(Token));

        await using var db = _h.NewDb();
        Assert.Empty(await db.RiskChecks.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x123")]
    [InlineData("not an address")]
    public async Task Invalid_address_is_rejected_before_any_source_is_asked(string address)
    {
        await Assert.ThrowsAsync<RiskInputException>(() => GetAsync(address));
        Assert.Equal(0, _h.GoPlus.Calls);
    }

    [Fact]
    public async Task Only_the_newest_checks_are_kept_and_exactly_one_is_latest()
    {
        for (var i = 0; i < 5; i++)
        {
            await GetAsync(Token, refresh: i > 0);
            _h.Time.Advance(TimeSpan.FromMinutes(10));
        }

        await using var db = _h.NewDb();
        var rows = await db.RiskChecks.OrderBy(c => c.CheckedAt).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(T0.AddMinutes(20), rows[0].CheckedAt);
        Assert.Single(rows, r => r.IsLatest);
        Assert.True(rows[^1].IsLatest);
    }

    [Fact]
    public async Task A_new_finding_is_recorded_as_a_change_against_the_previous_check()
    {
        var service = Service();
        await service.GetTokenAsync(Chain.Base, Token, false, default);
        Assert.Equal("[]", (await LatestRowAsync()).ChangesJson);

        _h.Time.Advance(TimeSpan.FromMinutes(10));
        _h.GoPlus.Reply = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        var result = await service.CheckTokenAsync(Chain.Base, Token, default);

        Assert.True(result.Comparison.ShouldAlert);
        var row = await LatestRowAsync();
        Assert.Contains("Appeared", row.ChangesJson);
        Assert.Contains(RiskCodes.Honeypot, row.ChangesJson);
        Assert.Equal(RiskLevel.Red, row.Level);
    }

    [Fact]
    public async Task The_first_check_of_a_token_has_no_changes_and_never_alerts()
    {
        _h.GoPlus.Reply = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        var result = await Service().CheckTokenAsync(Chain.Base, Token, default);

        Assert.True(result.Comparison.IsFirstCheck);
        Assert.False(result.Comparison.ShouldAlert);
        Assert.Equal("[]", (await LatestRowAsync()).ChangesJson);
    }

    [Fact]
    public async Task Summaries_read_the_cache_and_skip_unchecked_tokens_without_calling_a_source()
    {
        await GetAsync(Token);
        var callsBefore = _h.GoPlus.Calls;

        var summaries = await Service().TokenSummariesAsync(Chain.Base, [Token, Other], default);

        Assert.Equal(callsBefore, _h.GoPlus.Calls);
        Assert.Equal(RiskLevel.Green, summaries[Token].Level);
        Assert.False(summaries.ContainsKey(Other));
    }

    [Fact]
    public async Task Summary_lists_the_worst_findings_for_the_tooltip()
    {
        _h.GoPlus.Reply = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        await GetAsync(Token);

        var summary = (await Service().TokenSummariesAsync(Chain.Base, [Token], default))[Token];

        Assert.Equal(RiskLevel.Red, summary.Level);
        Assert.Contains("Honeypot or failing sell", summary.Top);
    }

    [Fact]
    public async Task Pool_summary_is_the_worst_of_its_two_cached_tokens_and_absent_when_one_is_missing_and_nothing_is_wrong()
    {
        _h.GoPlus.ByToken[Token] = RiskHarness.GoPlusOk() with { IsHoneypot = true };
        await GetAsync(Token);
        await GetAsync(Other);

        var both = await Service().PoolSummariesByAddressAsync(Chain.Base, [("0xpool", Token, Other)], default);
        Assert.Equal(RiskLevel.Red, both["0xpool"].Level);

        // One green token cached and the other never checked: half a verdict, so the row reads "not checked".
        var oneMissing = await Service().PoolSummariesByAddressAsync(Chain.Base, [("0xpool2", Other, "0x3333333333333333333333333333333333333333")], default);
        Assert.False(oneMissing.ContainsKey("0xpool2"));

        // ... but a red token is worth showing even when the other half is unknown.
        var redAndMissing = await Service().PoolSummariesByAddressAsync(Chain.Base, [("0xpool4", Token, "0x3333333333333333333333333333333333333333")], default);
        Assert.Equal(RiskLevel.Red, redAndMissing["0xpool4"].Level);

        var none = await Service().PoolSummariesByAddressAsync(Chain.Base, [("0xpool3", "0x4444444444444444444444444444444444444444", "0x3333333333333333333333333333333333333333")], default);
        Assert.False(none.ContainsKey("0xpool3"));
    }

    [Fact]
    public async Task Scope_holds_tokens_of_open_positions_and_starred_pools()
    {
        const string Owner = "0xabc0000000000000000000000000000000000001";
        await _h.AddHeldPositionAsync(Owner, "0xpoolheld", Token, Other);
        await using (var db = _h.NewDb())
        {
            db.StarredPools.Add(new StarredPool
            {
                Chain = Chain.Base, ExplorerId = "llama-1", Symbol = "CCC-DDD", Token0 = "0x5555555555555555555555555555555555555555",
                Token1 = Token, CreatedAt = T0,
            });
            await db.SaveChangesAsync();
        }

        var scope = await Service().ScopeAsync([Owner], default);

        Assert.Equal(3, scope.Tokens.Count);
        Assert.Contains("held", scope.Tokens[Token].Reasons);
        Assert.Contains("starred", scope.Tokens[Token].Reasons);
        Assert.Equal(["held"], scope.Tokens[Other].Reasons);
        Assert.Equal(["starred"], scope.Tokens["0x5555555555555555555555555555555555555555"].Reasons);
        Assert.Equal("AAA/BBB", scope.Pools["0xpoolheld"]);

        var held = await Service().GetHeldAsync([Owner], default);
        Assert.Equal(3, held.Count);
        Assert.All(held, r => Assert.Null(r.Risk));
    }

    [Fact]
    public async Task Scope_does_not_include_wallets_that_were_not_asked_for()
    {
        await _h.AddHeldPositionAsync("0xabc0000000000000000000000000000000000001", "0xp", Token, Other);

        Assert.Empty((await Service().ScopeAsync(["0xdef0000000000000000000000000000000000002"], default)).Tokens);
    }

    [Fact]
    public async Task Alert_state_and_liquidity_snapshots_round_trip_through_the_repository()
    {
        await using var db = _h.NewDb();
        var repo = new RiskRepository(db);
        repo.Add(new PoolLiquiditySnapshot { Chain = Chain.Base, PoolAddress = "0xp", At = T0, Token0Raw = System.Numerics.BigInteger.Parse("123456789012345678901234567890"), Token1Raw = 5 });
        repo.Add(new PoolLiquiditySnapshot { Chain = Chain.Base, PoolAddress = "0xp", At = T0.AddHours(30), Token0Raw = 1, Token1Raw = 1 });
        repo.Add(new RiskAlertState { Kind = RiskSubjectKind.Token, Chain = Chain.Base, Subject = Token, Code = "HONEYPOT", FirstAlertedAt = T0 });
        await db.SaveChangesAsync();

        // 24 h after T0 the first snapshot is 24 h old: outside a 6 h tolerance, inside a 30 h one.
        Assert.Null(await repo.GetSnapshotAtOrBeforeAsync(Chain.Base, "0xp", T0.AddHours(24), TimeSpan.FromHours(6), default));
        var wide = await repo.GetSnapshotAtOrBeforeAsync(Chain.Base, "0xp", T0.AddHours(24), TimeSpan.FromHours(30), default);
        Assert.Equal(System.Numerics.BigInteger.Parse("123456789012345678901234567890"), wide!.Token0Raw);
        Assert.Equal(T0.AddHours(30), (await repo.GetLatestSnapshotAsync(Chain.Base, "0xp", default))!.At);
        Assert.NotNull(await repo.FindOpenAlertAsync(RiskSubjectKind.Token, Chain.Base, Token, "HONEYPOT", default));

        await repo.PruneSnapshotsAsync(T0.AddHours(10), default);
        Assert.Equal(1, await db.PoolLiquiditySnapshots.CountAsync());
    }
}
