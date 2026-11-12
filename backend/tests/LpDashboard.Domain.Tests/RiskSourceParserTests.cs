using System.Text.Json;
using LpDashboard.Domain.Risk;
using LpDashboard.Infrastructure.Risk;

namespace LpDashboard.Domain.Tests;

/// <summary>
/// The source adapters parse recorded responses (Fixtures/risk): the *_aero and *_weth files are trimmed real answers from
/// 2026-11-11, the *_synthetic files are hand-made variants of them. No test calls the network.
/// </summary>
public class RiskSourceParserTests
{
    private const string Aero = "0x3f5bf3edac59d650d8fdf9657a71f2f904a3a4ee";
    private const string Weth = "0x4200000000000000000000000000000000000006";

    private static JsonElement Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "risk", name);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    // ---- GoPlus

    [Fact]
    public void GoPlus_real_response_is_parsed()
    {
        var outcome = GoPlusSource.Parse(Fixture("goplus_aero.json"), Aero);

        Assert.True(outcome.Answered);
        var f = outcome.Value!;
        Assert.False(f.IsHoneypot);
        Assert.False(f.CanMint);
        Assert.False(f.CanBlacklist);
        Assert.False(f.CanPauseTransfers);
        Assert.True(f.IsOpenSource);
        Assert.False(f.IsProxy);
        Assert.Equal(0m, f.BuyTaxPct);
        Assert.Equal(0m, f.SellTaxPct);
        Assert.Equal(14613, f.HolderCount);
        Assert.Equal("AERO", f.Symbol);
        Assert.Equal("0xcaf2c1251465c90d91a16b2288d9d6389f836a0e", f.CreatorAddress);
        Assert.Equal(0m, f.CreatorSharePct);
        // GoPlus gives the owner as an empty string when there is none.
        Assert.Null(f.OwnerAddress);
    }

    [Fact]
    public void GoPlus_percentages_become_zero_to_hundred_and_holders_keep_order()
    {
        var holders = GoPlusSource.Parse(Fixture("goplus_aero.json"), Aero).Value!.Holders!;

        Assert.Equal(10, holders.Count);
        Assert.Equal("0xbf7977e1521e9a3032bf7243ee4712f311f13915", holders[0].Address);
        Assert.Equal(49.9169044029338734m, holders[0].SharePct);
        Assert.True(holders[0].IsContract);
        Assert.False(holders[0].IsLocked);
        Assert.False(holders[2].IsContract);
    }

    [Fact]
    public void GoPlus_dex_pairs_mark_pool_holders()
    {
        // The synthetic response lists 0x1ad14e… as a UniV3 pair (copied from the real dex array) and holds tokens there.
        var holders = GoPlusSource.Parse(Fixture("goplus_scam_synthetic.json"), Aero).Value!.Holders!;

        Assert.False(holders.Single(h => h.Address.StartsWith("0x4444")).IsPool);
        Assert.True(holders.Single(h => h.Address.StartsWith("0x1ad14e")).IsPool);   // in the dex list
        Assert.True(holders.Single(h => h.Address.StartsWith("0x5555")).IsPool);     // tagged "Uniswap V3: Pool"
    }

    [Fact]
    public void GoPlus_weth_has_missing_fields_as_unknown_not_zero()
    {
        var f = GoPlusSource.Parse(Fixture("goplus_weth.json"), Weth).Value!;

        Assert.Null(f.BuyTaxPct);   // the field is absent in the real answer
        Assert.Null(f.SellTaxPct);
        Assert.Null(f.CreatorAddress);
        Assert.False(f.IsHoneypot);
        Assert.Equal(28.6101556971040575m, f.Holders![0].SharePct);
    }

    [Fact]
    public void GoPlus_bad_token_variant_feeds_the_evaluator_red()
    {
        var f = GoPlusSource.Parse(Fixture("goplus_scam_synthetic.json"), Aero).Value!;
        Assert.True(f.IsHoneypot);
        Assert.True(f.CanMint);
        Assert.Equal(99m, f.SellTaxPct);
        Assert.Equal(25m, f.CreatorSharePct);

        var facts = new TokenRiskFacts(Aero, f.Symbol, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
            [new SourceResult(RiskSources.GoPlus, true, true)], GoPlus: f);
        var a = RiskEvaluator.EvaluateToken(facts, new RiskOptions { TrustedTokens = [] });
        Assert.Equal(RiskLevel.Red, a.Verdict.Level);
        Assert.Contains(a.Findings, x => x.Code == RiskCodes.Honeypot);
        Assert.Contains(a.Findings, x => x.Code == RiskCodes.TopHolderConcentration && x.Severity == RiskSeverity.Red);
        Assert.Contains(a.Findings, x => x.Code == RiskCodes.UnverifiedSource);
    }

    [Fact]
    public void GoPlus_real_aero_shows_the_unlabelled_contract_holder_as_yellow_for_a_trusted_token()
    {
        var f = GoPlusSource.Parse(Fixture("goplus_aero.json"), Aero).Value!;
        var facts = new TokenRiskFacts(Aero, "AERO", new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
            [new SourceResult(RiskSources.GoPlus, true, true)], GoPlus: f);

        // 49.9% held by a contract GoPlus does not tag: trusted tokens get Yellow, an unknown token would get Red.
        var trusted = RiskEvaluator.EvaluateToken(facts, new RiskOptions());
        Assert.Equal(RiskSeverity.Yellow, trusted.Findings.Single(x => x.Code == RiskCodes.TopHolderConcentration).Severity);
        var unknown = RiskEvaluator.EvaluateToken(facts, new RiskOptions { TrustedTokens = [] });
        Assert.Equal(RiskSeverity.Red, unknown.Findings.Single(x => x.Code == RiskCodes.TopHolderConcentration).Severity);
    }

    [Fact]
    public void GoPlus_partial_answer_keeps_its_facts_but_does_not_count_as_a_check_that_ran()
    {
        // Real answer for a Uniswap v4 token (code 3, no is_honeypot flag), recorded 2026-11-11.
        var outcome = GoPlusSource.Parse(Fixture("goplus_v4_partial.json"), "0xb2000000000000000000008c031790942731b001");

        Assert.False(outcome.Answered);
        Assert.Equal("partial data (code 3)", outcome.Failure);
        Assert.NotNull(outcome.Value);
        Assert.Null(outcome.Value!.IsHoneypot);
        Assert.Equal("BASEMEME", outcome.Value.Symbol);
    }

    [Fact]
    public void GoPlus_v4_pool_manager_is_a_pool_not_a_whale()
    {
        var f = GoPlusSource.Parse(Fixture("goplus_v4_partial.json"), "0xb2000000000000000000008c031790942731b001").Value!;

        // 71.6% sits in Uniswap v4's PoolManager, which GoPlus lists as the pool_manager of the token's pools.
        var manager = f.Holders![0];
        Assert.Equal("0x498581ff718922c3f8e6a244956af099b2652b2b", manager.Address);
        Assert.Equal(71.5593977941851844m, manager.SharePct);
        Assert.True(manager.IsPool);

        var facts = new TokenRiskFacts("0xb2000000000000000000008c031790942731b001", f.Symbol, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
            [new SourceResult(RiskSources.GoPlus, true, false, "partial data (code 3)")], GoPlus: f);
        var assessment = RiskEvaluator.EvaluateToken(facts, new RiskOptions());
        Assert.DoesNotContain(assessment.Findings, x => x.Code == RiskCodes.TopHolderConcentration);
        // A required source only partly answered: never green.
        Assert.NotEqual(RiskLevel.Green, assessment.Verdict.Level);
    }

    [Theory]
    [InlineData("goplus_empty.json", "token not known to GoPlus")]
    [InlineData("goplus_ratelimited.json", "rate limited")]
    public void GoPlus_unanswered_cases_name_the_reason(string fixture, string reason)
    {
        var outcome = GoPlusSource.Parse(Fixture(fixture), Aero);
        Assert.False(outcome.Answered);
        Assert.Equal(reason, outcome.Failure);
    }

    [Fact]
    public void GoPlus_ignores_a_reply_for_another_address_and_non_object_roots()
    {
        Assert.False(GoPlusSource.Parse(Fixture("goplus_weth.json"), Aero).Answered);
        Assert.False(GoPlusSource.Parse(JsonDocument.Parse("[]").RootElement, Aero).Answered);
    }

    [Fact]
    public void Malformed_body_never_reaches_a_parser_as_a_document()
    {
        // RiskHttp turns this into the failure "unreadable response" instead of passing a document on.
        Assert.ThrowsAny<JsonException>(() => Fixture("malformed.json"));
    }

    // ---- honeypot.is

    [Fact]
    public void Honeypot_real_response_is_parsed()
    {
        var outcome = HoneypotIsSource.Parse(Fixture("honeypot_aero.json"));

        Assert.True(outcome.Answered);
        Assert.False(outcome.Value!.IsHoneypot);
        Assert.Equal(0m, outcome.Value.BuyTaxPct);
        Assert.Equal(0m, outcome.Value.SellTaxPct);
        Assert.Null(outcome.Value.Reason);
    }

    [Fact]
    public void Honeypot_flagged_variant_carries_taxes_and_reason()
    {
        var f = HoneypotIsSource.Parse(Fixture("honeypot_scam_synthetic.json")).Value!;

        Assert.True(f.IsHoneypot);
        Assert.Equal(99m, f.SellTaxPct);
        Assert.Equal(4m, f.BuyTaxPct);
        Assert.Equal("SELL_FAILED", f.Reason);
    }

    [Fact]
    public void Honeypot_without_a_simulation_gives_no_verdict()
    {
        var outcome = HoneypotIsSource.Parse(Fixture("honeypot_nosim.json"));
        Assert.False(outcome.Answered);
        Assert.Contains("No pairs found", outcome.Failure);
    }

    [Fact]
    public void Honeypot_empty_object_gives_no_verdict() =>
        Assert.False(HoneypotIsSource.Parse(JsonDocument.Parse("{}").RootElement).Answered);

    // ---- DexScreener

    [Fact]
    public void DexScreener_real_response_sums_the_tokens_pairs()
    {
        var outcome = DexScreenerSource.ParseToken(Fixture("dexscreener_aero.json"), Aero, "base");

        Assert.True(outcome.Answered);
        var m = outcome.Value!;
        Assert.Equal(4, m.PoolAddresses.Count);
        Assert.Contains("0xd84d4c6cf3dacfe46847b633553a7ec667328a25", m.PoolAddresses);
        Assert.Equal(5888.85m + 2164.58m + 633.33m + 1199.84m, m.LiquidityUsd);
        Assert.True(m.Volume24hUsd > 0);
        Assert.True(m.Trades24h > 0);
        // The oldest of the four pairs: 1792629707000 ms.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792629707000).UtcDateTime, m.FirstPoolCreatedAt);
    }

    [Fact]
    public void DexScreener_ignores_other_chains_and_other_tokens()
    {
        Assert.Equal(0m, DexScreenerSource.ParseToken(Fixture("dexscreener_aero.json"), Aero, "ethereum").Value!.LiquidityUsd);
        Assert.Equal(0m, DexScreenerSource.ParseToken(Fixture("dexscreener_aero.json"), "0x9999999999999999999999999999999999999999", "base").Value!.LiquidityUsd);
    }

    [Fact]
    public void DexScreener_null_pairs_is_an_answer_of_zero_liquidity()
    {
        var m = DexScreenerSource.ParseToken(Fixture("dexscreener_none.json"), Aero, "base").Value!;
        Assert.Equal(0m, m.LiquidityUsd);
        Assert.Null(m.FirstPoolCreatedAt);
        Assert.Empty(m.PoolAddresses);
    }

    [Fact]
    public void DexScreener_garbage_is_not_an_answer() =>
        Assert.False(DexScreenerSource.ParseToken(JsonDocument.Parse("[1,2]").RootElement, Aero, "base").Answered);

    [Fact]
    public void DexScreener_pool_lookup_reads_creation_time_and_liquidity()
    {
        var f = DexScreenerSource.ParsePool(Fixture("dexscreener_pair_synthetic.json"), "base").Value!;
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792710629000).UtcDateTime, f.CreatedAt);
        Assert.Equal(12345.5m, f.LiquidityUsd);
    }

    [Fact]
    public void DexScreener_unindexed_pool_is_not_an_answer()
    {
        var outcome = DexScreenerSource.ParsePool(JsonDocument.Parse("{\"pairs\":[]}").RootElement, "base");
        Assert.False(outcome.Answered);
        Assert.Equal("pool not indexed", outcome.Failure);
    }

    // ---- Etherscan (synthetic: the source could not be reached from the build machine)

    [Fact]
    public void Etherscan_source_flag_follows_the_source_code() =>
        Assert.Equal([true, false], new[] { "etherscan_verified_synthetic.json", "etherscan_unverified_synthetic.json" }
            .Select(f => EtherscanSource.ParseSource(Fixture(f))).Cast<bool>().ToArray());

    [Fact]
    public void Etherscan_api_error_is_not_a_verdict() =>
        Assert.Null(EtherscanSource.ParseSource(Fixture("etherscan_notok_synthetic.json")));

    [Fact]
    public void Etherscan_creation_time_is_read() =>
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792710629).UtcDateTime, EtherscanSource.ParseCreation(Fixture("etherscan_creation_synthetic.json")));

    // ---- on-chain helpers

    [Theory]
    [InlineData("0x", false)]
    [InlineData("0x6080604052", true)]
    [InlineData("0xef01000000000000000000000000000000000000000001", false)] // EIP-7702 delegation: still one key
    public void Contract_code_check_ignores_eip7702_delegations(string code, bool contract) =>
        Assert.Equal(contract, LpDashboard.Infrastructure.Risk.EvmOnChainFactsSource.HasContractCode(code));
}
