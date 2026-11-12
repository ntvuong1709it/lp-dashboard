using LpDashboard.Domain.Risk;

namespace LpDashboard.Domain.Tests;

internal static class RiskTestData
{
    public const string Token = "0x1111111111111111111111111111111111111111";
    public const string EoaOwner = "0x2222222222222222222222222222222222222222";
    public const string SafeOwner = "0x3333333333333333333333333333333333333333";
    public const string Whale = "0x4444444444444444444444444444444444444444";
    public const string PoolAddr = "0x5555555555555555555555555555555555555555";
    public const string Impl = "0x6666666666666666666666666666666666666666";
    public const string Admin = "0x7777777777777777777777777777777777777777";
    public static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    public static SourceResult Ok(string name, bool required = false) => new(name, required, true);
    public static SourceResult Down(string name, bool required = false, string? why = "HTTP 429") => new(name, required, false, why);

    /// <summary>All three required sources answered, plus DexScreener and prices.</summary>
    public static SourceResult[] AllAnswered() =>
    [
        Ok(RiskSources.GoPlus, true), Ok(RiskSources.HoneypotIs, true), Ok(RiskSources.Rpc, true),
        Ok(RiskSources.DexScreener), Ok(RiskSources.Prices),
    ];

    public static GoPlusFacts GoPlus(
        bool? honeypot = false, decimal? buy = 0, decimal? sell = 0, bool? mint = false, bool? blacklist = false, bool? pause = false,
        string? owner = null, string? creator = null, decimal? creatorShare = 0, bool? openSource = true, bool? proxy = false,
        IReadOnlyList<HolderFact>? holders = null) =>
        new(honeypot, buy, sell, mint, blacklist, pause, owner, creator, creatorShare, openSource, proxy, 1000, holders ?? []);

    public static HoneypotIsFacts HoneypotIs(bool? honeypot = false, bool? failed = false, decimal? buy = 0, decimal? sell = 0, string? reason = null) =>
        new(honeypot, failed, buy, sell, reason);

    public static OnChainFacts OnChain(string? owner = RiskAddresses.Zero, bool? ownerIsContract = null, string? impl = null, string? admin = null, bool? adminIsContract = null) =>
        new(owner, ownerIsContract, impl, admin, adminIsContract);

    public static MarketFacts Market(decimal liquidity = 1_000_000, decimal volume = 100_000, int trades = 5000, DateTime? firstPool = null, params string[] pools) =>
        new(liquidity, volume, trades, firstPool ?? Now.AddDays(-400), pools);

    /// <summary>A token with every check run and nothing wrong.</summary>
    public static TokenRiskFacts Clean(string address = Token, string symbol = "TKN") => new(
        address, symbol, Now, AllAnswered(),
        GoPlus: GoPlus(), HoneypotIs: HoneypotIs(), OnChain: OnChain(), Market: Market(), Prices: new PriceFacts(2m, true));

    public static HolderFact Holder(string address, decimal pct, bool contract = false, bool locked = false, bool pool = false) =>
        new(address, pct, contract, locked, pool);

    public static string Addr(int n) => "0x" + n.ToString("x40");
}

public class RiskEvaluatorTests
{
    private static readonly RiskOptions Options = new();

    private static TokenRiskAssessment Eval(TokenRiskFacts f, RiskOptions? o = null) => RiskEvaluator.EvaluateToken(f, o ?? Options);

    private static RiskFinding? Find(TokenRiskAssessment a, string code) => a.Findings.FirstOrDefault(f => f.Code == code);

    private static void AssertNo(TokenRiskAssessment a, string code) => Assert.Null(Find(a, code));

    private static void AssertSeverity(TokenRiskAssessment a, string code, RiskSeverity severity) =>
        Assert.Equal(severity, Assert.IsType<RiskFinding>(Find(a, code)).Severity);

    // ---- verdict

    [Fact]
    public void Clean_token_with_all_sources_is_green()
    {
        var a = Eval(RiskTestData.Clean());
        Assert.Equal(RiskLevel.Green, a.Verdict.Level);
        Assert.Equal(5, a.Verdict.ChecksRun);
        Assert.DoesNotContain(a.Findings, f => f.Severity != RiskSeverity.Info);
    }

    [Fact]
    public void Missing_required_source_is_partial_never_green()
    {
        var f = RiskTestData.Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.HoneypotIs, true, "timeout"), RiskTestData.Ok(RiskSources.Rpc, true), RiskTestData.Ok(RiskSources.DexScreener)],
            HoneypotIs = null,
        };
        var a = Eval(f);
        Assert.Equal(RiskLevel.Partial, a.Verdict.Level);
        Assert.Equal(3, a.Verdict.ChecksRun);
        Assert.Equal(4, a.Verdict.ChecksTotal);
        var info = Find(a, RiskCodes.SourceUnavailable)!;
        Assert.Equal(RiskSeverity.Info, info.Severity);
        Assert.Contains("honeypot.is (timeout)", info.Evidence);
        Assert.Contains("3 of 4", info.Evidence);
    }

    [Fact]
    public void Missing_optional_source_stays_green_but_is_reported()
    {
        var f = RiskTestData.Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Ok(RiskSources.HoneypotIs, true), RiskTestData.Ok(RiskSources.Rpc, true), RiskTestData.Down(RiskSources.DexScreener)],
            Market = null,
        };
        var a = Eval(f);
        Assert.Equal(RiskLevel.Green, a.Verdict.Level);
        Assert.NotNull(Find(a, RiskCodes.SourceUnavailable));
    }

    [Fact]
    public void Nothing_answered_is_not_checked()
    {
        var f = RiskTestData.Clean() with
        {
            Sources = [RiskTestData.Down(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.Rpc, true)],
            GoPlus = null, HoneypotIs = null, OnChain = null, Market = null, Prices = null,
        };
        Assert.Equal(RiskLevel.NotChecked, Eval(f).Verdict.Level);
    }

    [Fact]
    public void A_red_finding_wins_even_when_sources_are_missing()
    {
        var f = RiskTestData.Clean() with
        {
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Down(RiskSources.HoneypotIs, true), RiskTestData.Down(RiskSources.Rpc, true)],
            GoPlus = RiskTestData.GoPlus(honeypot: true), HoneypotIs = null, OnChain = null,
        };
        Assert.Equal(RiskLevel.Red, Eval(f).Verdict.Level);
    }

    [Fact]
    public void Pool_verdict_is_the_worst_of_its_tokens_and_its_own_findings()
    {
        var green = Eval(RiskTestData.Clean());
        var red = Eval(RiskTestData.Clean(RiskTestData.Addr(9)) with { GoPlus = RiskTestData.GoPlus(honeypot: true) });
        var yellow = Eval(RiskTestData.Clean(RiskTestData.Addr(8)) with { Market = RiskTestData.Market(liquidity: 5_000) });
        var pool = PoolFacts();

        Assert.Equal(RiskLevel.Red, RiskEvaluator.EvaluatePool(pool, green, red, Options).Verdict.Level);
        Assert.Equal(RiskLevel.Yellow, RiskEvaluator.EvaluatePool(pool, green, yellow, Options).Verdict.Level);
        Assert.Equal(RiskLevel.Green, RiskEvaluator.EvaluatePool(pool, green, green, Options).Verdict.Level);

        var young = pool with { PoolCreatedAt = RiskTestData.Now.AddDays(-1) };
        Assert.Equal(RiskLevel.Yellow, RiskEvaluator.EvaluatePool(young, green, green, Options).Verdict.Level);
    }

    [Fact]
    public void Pool_with_an_unchecked_token_is_partial_not_green()
    {
        var green = Eval(RiskTestData.Clean());
        var pool = RiskEvaluator.EvaluatePool(PoolFacts(), green, null, Options);
        Assert.Equal(RiskLevel.Partial, pool.Verdict.Level);
        // ... but a red token still shows as red.
        var red = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) });
        Assert.Equal(RiskLevel.Red, RiskEvaluator.EvaluatePool(PoolFacts(), red, null, Options).Verdict.Level);
    }

    // ---- HONEYPOT

    [Fact]
    public void Honeypot_is_flag_is_red()
    {
        var a = Eval(RiskTestData.Clean() with { HoneypotIs = RiskTestData.HoneypotIs(honeypot: true, reason: "SELL_REVERTED") });
        AssertSeverity(a, RiskCodes.Honeypot, RiskSeverity.Red);
        Assert.Contains("SELL_REVERTED", Find(a, RiskCodes.Honeypot)!.Evidence);
    }

    [Fact]
    public void Failing_sell_simulation_is_red() =>
        AssertSeverity(Eval(RiskTestData.Clean() with { HoneypotIs = RiskTestData.HoneypotIs(failed: true) }), RiskCodes.Honeypot, RiskSeverity.Red);

    [Fact]
    public void GoPlus_honeypot_flag_is_red() =>
        AssertSeverity(Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(honeypot: true) }), RiskCodes.Honeypot, RiskSeverity.Red);

    [Theory]
    [InlineData(10, false)] // exactly at the limit is a tax, not a honeypot
    [InlineData(10.01, true)]
    [InlineData(99, true)]
    public void Sell_tax_above_ten_percent_is_a_honeypot(double tax, bool honeypot)
    {
        var a = Eval(RiskTestData.Clean() with { HoneypotIs = RiskTestData.HoneypotIs(sell: (decimal)tax) });
        Assert.Equal(honeypot, Find(a, RiskCodes.Honeypot) is not null);
    }

    [Fact]
    public void Honeypot_takes_the_higher_tax_of_the_two_sources()
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(sell: 1), HoneypotIs = RiskTestData.HoneypotIs(sell: 40) });
        Assert.Contains("40", Find(a, RiskCodes.Honeypot)!.Evidence);
        Assert.Contains("honeypot.is", Find(a, RiskCodes.Honeypot)!.Evidence);
    }

    // ---- SELL_TAX / BUY_TAX

    [Theory]
    [InlineData(2.99, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    [InlineData(10.5, false)] // above ten it is HONEYPOT, not SELL_TAX
    public void Sell_tax_is_yellow_between_three_and_ten(double tax, bool yellow)
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(sell: (decimal)tax) });
        Assert.Equal(yellow, Find(a, RiskCodes.SellTax) is not null);
        if (yellow) AssertSeverity(a, RiskCodes.SellTax, RiskSeverity.Yellow);
    }

    [Theory]
    [InlineData(2.99, false)]
    [InlineData(3, true)]
    [InlineData(25, true)]
    public void Buy_tax_is_yellow_from_three_percent(double tax, bool yellow)
    {
        var a = Eval(RiskTestData.Clean() with { HoneypotIs = RiskTestData.HoneypotIs(buy: (decimal)tax) });
        Assert.Equal(yellow, Find(a, RiskCodes.BuyTax) is not null);
        if (yellow) AssertSeverity(a, RiskCodes.BuyTax, RiskSeverity.Yellow);
    }

    // ---- PRIVILEGED_MINT_OR_BLACKLIST

    [Fact]
    public void Mintable_token_owned_by_an_eoa_is_red()
    {
        var a = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(mint: true),
            OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false),
        });
        AssertSeverity(a, RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Red);
        Assert.Equal(RiskTestData.EoaOwner, Find(a, RiskCodes.PrivilegedMintOrBlacklist)!.Subject);
        Assert.Contains("mint", Find(a, RiskCodes.PrivilegedMintOrBlacklist)!.Evidence);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Blacklist_pause_and_mint_each_trigger_it(bool mint, bool blacklist, bool pause)
    {
        var a = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(mint: mint, blacklist: blacklist, pause: pause),
            OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false),
        });
        AssertSeverity(a, RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Red);
    }

    [Fact]
    public void Mintable_token_owned_by_a_contract_is_yellow()
    {
        var a = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(blacklist: true),
            OnChain = RiskTestData.OnChain(owner: RiskTestData.SafeOwner, ownerIsContract: true),
        });
        AssertSeverity(a, RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Yellow);
    }

    [Fact]
    public void Controller_of_unknown_type_is_yellow_not_red()
    {
        var a = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(mint: true, owner: RiskTestData.EoaOwner),
            OnChain = null,
            Sources = [RiskTestData.Ok(RiskSources.GoPlus, true), RiskTestData.Ok(RiskSources.HoneypotIs, true), RiskTestData.Down(RiskSources.Rpc, true)],
        });
        AssertSeverity(a, RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Yellow);
    }

    [Fact]
    public void Renounced_owner_means_no_privilege_finding()
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(mint: true, blacklist: true), OnChain = RiskTestData.OnChain(owner: RiskAddresses.Zero) });
        AssertNo(a, RiskCodes.PrivilegedMintOrBlacklist);
        AssertNo(a, RiskCodes.OwnerNotRenounced);
    }

    [Fact]
    public void Proxy_admin_is_the_controller_when_there_is_no_owner()
    {
        var a = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(mint: true),
            OnChain = RiskTestData.OnChain(owner: null, impl: RiskTestData.Impl, admin: RiskTestData.Admin, adminIsContract: false),
        });
        var f = Find(a, RiskCodes.PrivilegedMintOrBlacklist)!;
        Assert.Equal(RiskSeverity.Red, f.Severity);
        Assert.Equal(RiskTestData.Admin, f.Subject);
        Assert.Contains("proxy admin", f.Evidence);
    }

    [Fact]
    public void Powers_without_an_identifiable_controller_are_yellow()
    {
        // No owner() function at all (role-based access): we cannot say who, but the powers exist.
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(mint: true), OnChain = RiskTestData.OnChain(owner: null) });
        AssertSeverity(a, RiskCodes.PrivilegedMintOrBlacklist, RiskSeverity.Yellow);
    }

    [Fact]
    public void Trusted_tokens_are_exempt_from_privilege_proxy_and_owner_rules()
    {
        var f = RiskTestData.Clean(RiskTestData.Token) with
        {
            GoPlus = RiskTestData.GoPlus(blacklist: true, pause: true),
            OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false, impl: RiskTestData.Impl, admin: RiskTestData.Admin, adminIsContract: true),
        };
        var trusted = new RiskOptions { TrustedTokens = [RiskTestData.Token.ToUpperInvariant().Replace("0X", "0x")] };
        var a = Eval(f, trusted);
        AssertNo(a, RiskCodes.PrivilegedMintOrBlacklist);
        AssertNo(a, RiskCodes.UpgradeableProxy);
        AssertNo(a, RiskCodes.OwnerNotRenounced);
        AssertSeverity(a, RiskCodes.TrustedToken, RiskSeverity.Info);
        Assert.True(a.Trusted);
        Assert.Equal(RiskLevel.Green, a.Verdict.Level);

        // Not on the list: the same facts are red.
        Assert.Equal(RiskLevel.Red, Eval(f).Verdict.Level);
    }

    [Fact]
    public void Trusted_tokens_still_get_honeypot_and_concentration_checks()
    {
        var f = RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(honeypot: true, holders: [RiskTestData.Holder(RiskTestData.Whale, 60)]),
        };
        var a = Eval(f, new RiskOptions { TrustedTokens = [RiskTestData.Token] });
        AssertSeverity(a, RiskCodes.Honeypot, RiskSeverity.Red);
        AssertSeverity(a, RiskCodes.TopHolderConcentration, RiskSeverity.Red);
    }

    // ---- OWNER_NOT_RENOUNCED

    [Fact]
    public void Owner_without_dangerous_powers_is_info_only()
    {
        var a = Eval(RiskTestData.Clean() with { OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false) });
        AssertSeverity(a, RiskCodes.OwnerNotRenounced, RiskSeverity.Info);
        Assert.Equal(RiskLevel.Green, a.Verdict.Level);
    }

    [Fact]
    public void Owner_not_renounced_is_not_added_when_the_privilege_finding_already_names_the_owner()
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(mint: true), OnChain = RiskTestData.OnChain(owner: RiskTestData.EoaOwner, ownerIsContract: false) });
        AssertNo(a, RiskCodes.OwnerNotRenounced);
    }

    // ---- TOP_HOLDER_CONCENTRATION / TOP10

    [Theory]
    [InlineData(29.9, false)]
    [InlineData(30, false)] // the limit itself is allowed, above it is red
    [InlineData(30.1, true)]
    public void Largest_holder_above_thirty_percent_is_red(double pct, bool red)
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(holders: [RiskTestData.Holder(RiskTestData.Whale, (decimal)pct)]) });
        Assert.Equal(red, Find(a, RiskCodes.TopHolderConcentration) is not null);
        if (red)
        {
            Assert.Equal(RiskTestData.Whale, Find(a, RiskCodes.TopHolderConcentration)!.Subject);
            Assert.Contains(RiskTestData.Whale, Find(a, RiskCodes.TopHolderConcentration)!.Evidence);
        }
    }

    [Theory]
    [InlineData(19.9, false)]
    [InlineData(20, false)]
    [InlineData(20.1, true)]
    public void Creator_above_twenty_percent_is_red(double pct, bool red)
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(creator: RiskTestData.EoaOwner, creatorShare: (decimal)pct) });
        Assert.Equal(red, Find(a, RiskCodes.TopHolderConcentration) is not null);
    }

    [Fact]
    public void Pools_burn_addresses_the_token_itself_and_locked_holders_are_not_concentration()
    {
        var holders = new[]
        {
            RiskTestData.Holder(RiskTestData.PoolAddr, 40, contract: true),                    // listed by DexScreener below
            RiskTestData.Holder(RiskAddresses.Dead, 35),                                         // burn
            RiskTestData.Holder(RiskTestData.Token, 33, contract: true),                         // the token itself
            RiskTestData.Holder(RiskTestData.SafeOwner, 32, contract: true, locked: true),       // locker
            RiskTestData.Holder(RiskTestData.Addr(77), 31, contract: true, pool: true),          // tagged as a pool
            RiskTestData.Holder(RiskTestData.Whale, 5),
        };
        var f = RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(holders: holders),
            Market = RiskTestData.Market(pools: RiskTestData.PoolAddr),
        };
        var a = Eval(f);
        AssertNo(a, RiskCodes.TopHolderConcentration);
        AssertNo(a, RiskCodes.Top10Concentration);
        Assert.Equal(RiskTestData.Whale, RiskFactsAnalysis.TopHolder(f)!.Address);
    }

    [Fact]
    public void Creator_that_is_a_burn_address_or_locked_does_not_count()
    {
        var burnt = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(creator: RiskAddresses.Zero, creatorShare: 90) });
        AssertNo(burnt, RiskCodes.TopHolderConcentration);

        var locked = Eval(RiskTestData.Clean() with
        {
            GoPlus = RiskTestData.GoPlus(creator: RiskTestData.SafeOwner, creatorShare: 90, holders: [RiskTestData.Holder(RiskTestData.SafeOwner, 90, contract: true, locked: true)]),
        });
        AssertNo(locked, RiskCodes.TopHolderConcentration);
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(50.5, true)]
    public void Top_ten_above_fifty_percent_is_yellow(double lastHolderPct, bool yellow)
    {
        // Ten holders, none above 30%: 9 x 5 = 45, plus the last one.
        var holders = Enumerable.Range(1, 9).Select(i => RiskTestData.Holder(RiskTestData.Addr(100 + i), 5)).ToList();
        holders.Add(RiskTestData.Holder(RiskTestData.Addr(200), (decimal)lastHolderPct - 45m));
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(holders: holders) });
        Assert.Equal(yellow, Find(a, RiskCodes.Top10Concentration) is not null);
        if (yellow) AssertSeverity(a, RiskCodes.Top10Concentration, RiskSeverity.Yellow);
    }

    [Fact]
    public void Only_the_first_ten_holders_count_towards_top_ten()
    {
        var holders = Enumerable.Range(1, 12).Select(i => RiskTestData.Holder(RiskTestData.Addr(100 + i), 10)).ToList();
        Assert.Equal(100m, RiskFactsAnalysis.Top10Share(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(holders: holders) }));
    }

    [Fact]
    public void No_holder_list_means_no_concentration_finding()
    {
        var f = RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus() with { Holders = null } };
        var a = Eval(f);
        AssertNo(a, RiskCodes.TopHolderConcentration);
        AssertNo(a, RiskCodes.Top10Concentration);
    }

    // ---- UNVERIFIED_SOURCE / UPGRADEABLE_PROXY

    [Fact]
    public void Unverified_source_is_yellow_from_goplus_or_basescan()
    {
        AssertSeverity(Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(openSource: false) }), RiskCodes.UnverifiedSource, RiskSeverity.Yellow);
        AssertSeverity(Eval(RiskTestData.Clean() with { Contract = new ContractFacts(false, null) }), RiskCodes.UnverifiedSource, RiskSeverity.Yellow);
    }

    [Fact]
    public void Basescan_verification_overrules_goplus()
    {
        var a = Eval(RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(openSource: false), Contract = new ContractFacts(true, null) });
        AssertNo(a, RiskCodes.UnverifiedSource);
    }

    [Fact]
    public void Upgradeable_proxy_is_yellow_and_names_the_admin()
    {
        var a = Eval(RiskTestData.Clean() with { OnChain = RiskTestData.OnChain(impl: RiskTestData.Impl, admin: RiskTestData.Admin, adminIsContract: false) });
        var f = Find(a, RiskCodes.UpgradeableProxy)!;
        Assert.Equal(RiskSeverity.Yellow, f.Severity);
        Assert.Contains(RiskTestData.Impl, f.Evidence);
        Assert.Contains(RiskTestData.Admin, f.Evidence);
        Assert.Contains("externally owned", f.Evidence);
    }

    [Fact]
    public void Zero_implementation_slot_is_not_a_proxy() =>
        AssertNo(Eval(RiskTestData.Clean() with { OnChain = RiskTestData.OnChain(impl: RiskAddresses.Zero) }), RiskCodes.UpgradeableProxy);

    // ---- TOKEN_YOUNG / THIN_LIQUIDITY / WASH_SUSPECT

    [Theory]
    [InlineData(-6.9, true)]
    [InlineData(-7, false)]
    [InlineData(-30, false)]
    public void First_pool_under_seven_days_is_young(double days, bool young)
    {
        var a = Eval(RiskTestData.Clean() with { Market = RiskTestData.Market(firstPool: RiskTestData.Now.AddDays(days)) });
        Assert.Equal(young, Find(a, RiskCodes.TokenYoung) is not null);
        if (young) AssertSeverity(a, RiskCodes.TokenYoung, RiskSeverity.Yellow);
    }

    [Fact]
    public void Contract_created_under_seven_days_ago_is_young_too() =>
        AssertSeverity(Eval(RiskTestData.Clean() with { Contract = new ContractFacts(true, RiskTestData.Now.AddDays(-2)) }), RiskCodes.TokenYoung, RiskSeverity.Yellow);

    [Theory]
    [InlineData(99_999, true)]
    [InlineData(100_000, false)]
    public void Liquidity_under_one_hundred_thousand_is_thin(double liquidity, bool thin)
    {
        var a = Eval(RiskTestData.Clean() with { Market = RiskTestData.Market(liquidity: (decimal)liquidity, volume: 1000) });
        Assert.Equal(thin, Find(a, RiskCodes.ThinLiquidity) is not null);
    }

    [Theory]
    [InlineData(10_000_001, 99, true)]   // above 10x liquidity, under 100 trades
    [InlineData(10_000_000, 99, false)]  // exactly 10x is not above
    [InlineData(10_000_001, 100, false)] // enough trades
    public void High_volume_on_few_trades_is_a_wash_suspect(double volume, int trades, bool suspect)
    {
        var a = Eval(RiskTestData.Clean() with { Market = RiskTestData.Market(liquidity: 1_000_000, volume: (decimal)volume, trades: trades) });
        Assert.Equal(suspect, Find(a, RiskCodes.WashSuspect) is not null);
    }

    // ---- PRICE_HISTORY_MISSING / STABLE_DEPEG

    [Fact]
    public void Missing_price_history_is_yellow() =>
        AssertSeverity(Eval(RiskTestData.Clean() with { Prices = new PriceFacts(2m, false) }), RiskCodes.PriceHistoryMissing, RiskSeverity.Yellow);

    [Theory]
    [InlineData("USDC", 0.9899, true)]
    [InlineData("USDC", 0.99, false)] // exactly 1% is not beyond it
    [InlineData("USDC", 1.0101, true)]
    [InlineData("WETH", 0.5, false)]  // not a stablecoin
    public void Stablecoin_more_than_one_percent_off_is_a_depeg(string symbol, double price, bool depeg)
    {
        var a = Eval(RiskTestData.Clean(symbol: symbol) with { Prices = new PriceFacts((decimal)price, true) });
        Assert.Equal(depeg, Find(a, RiskCodes.StableDepeg) is not null);
    }

    // ---- options

    [Fact]
    public void Thresholds_come_from_options()
    {
        var f = RiskTestData.Clean() with { GoPlus = RiskTestData.GoPlus(holders: [RiskTestData.Holder(RiskTestData.Whale, 45)]) };
        AssertSeverity(Eval(f), RiskCodes.TopHolderConcentration, RiskSeverity.Red);
        AssertNo(Eval(f, new RiskOptions { TopHolderRedPct = 50 }), RiskCodes.TopHolderConcentration);
    }

    // ---- pool findings

    private static PoolRiskFacts PoolFacts(
        DateTime? created = null, decimal? fee = 10, decimal? reward = 0, bool? gauge = null, LpConcentrationFacts? lp = null) =>
        new(RiskTestData.PoolAddr, RiskTestData.Now, [RiskTestData.Ok(RiskSources.DexScreener, true), RiskTestData.Ok(RiskSources.PoolStats)],
            created ?? RiskTestData.Now.AddDays(-100), fee, reward, gauge, lp);

    private static RiskFinding? PoolFinding(PoolRiskFacts f, string code)
    {
        var green = Eval(RiskTestData.Clean());
        return RiskEvaluator.EvaluatePool(f, green, green, Options).Findings.FirstOrDefault(x => x.Code == code);
    }

    [Theory]
    [InlineData(29.9, null)]
    [InlineData(30, RiskSeverity.Yellow)]
    [InlineData(49.9, RiskSeverity.Yellow)]
    [InlineData(50, RiskSeverity.Red)]
    [InlineData(90, RiskSeverity.Red)]
    public void Lp_concentration_is_yellow_at_thirty_and_red_at_fifty(double share, RiskSeverity? expected)
    {
        var f = PoolFinding(PoolFacts(lp: new LpConcentrationFacts(RiskTestData.Whale, (decimal)share, false)), RiskCodes.LpConcentration)!;
        Assert.Equal(expected ?? RiskSeverity.Info, f.Severity);
        if (expected is not null) Assert.Contains(RiskTestData.Whale, f.Evidence);
    }

    [Fact]
    public void Lp_concentration_without_an_index_reads_not_checked_and_does_not_block_green()
    {
        var green = Eval(RiskTestData.Clean());
        var a = RiskEvaluator.EvaluatePool(PoolFacts(), green, green, Options);
        var f = a.Findings.Single(x => x.Code == RiskCodes.LpConcentration);
        Assert.Equal(RiskSeverity.Info, f.Severity);
        Assert.Contains("not checked", f.Title);
        Assert.Equal(RiskLevel.Green, a.Verdict.Level);
    }

    [Fact]
    public void Partial_index_history_is_stated_in_the_evidence()
    {
        var f = PoolFinding(PoolFacts(lp: new LpConcentrationFacts(RiskTestData.Whale, 60, true)), RiskCodes.LpConcentration)!;
        Assert.Equal(RiskSeverity.Red, f.Severity);
        Assert.Contains("partial history", f.Evidence);
    }

    [Theory]
    [InlineData(-6.9, true)]
    [InlineData(-7, false)]
    public void Pool_under_seven_days_is_young(double days, bool young) =>
        Assert.Equal(young, PoolFinding(PoolFacts(created: RiskTestData.Now.AddDays(days)), RiskCodes.PoolYoung) is not null);

    [Theory]
    [InlineData(10, 40, false)]  // 80% exactly is not above 80%
    [InlineData(10, 41, true)]   // ~80.4%
    [InlineData(0, 25, true)]    // all emissions
    [InlineData(10, 0, false)]
    public void Pool_living_on_emissions_is_yellow_above_eighty_percent(double fee, double reward, bool flagged) =>
        Assert.Equal(flagged, PoolFinding(PoolFacts(fee: (decimal)fee, reward: (decimal)reward), RiskCodes.EmissionDependent) is not null);

    [Fact]
    public void Killed_gauge_is_yellow_but_a_missing_gauge_is_not()
    {
        Assert.Equal(RiskSeverity.Yellow, PoolFinding(PoolFacts(gauge: false), RiskCodes.GaugeKilled)!.Severity);
        Assert.Null(PoolFinding(PoolFacts(gauge: true), RiskCodes.GaugeKilled));
        Assert.Null(PoolFinding(PoolFacts(gauge: null), RiskCodes.GaugeKilled));
    }
}
