using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class PortfolioInsightTests
{
    private const string Aero = "0xaero";
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Token Tok(string symbol) => new() { Symbol = symbol, Address = "0x" + symbol.ToLowerInvariant(), Decimals = 18 };

    private static Position Pos(
        string s0, string s1, decimal a0, decimal a1, decimal? p0, decimal? p1,
        PositionStatus status = PositionStatus.Open,
        decimal unc0 = 0, decimal unc1 = 0, decimal pending = 0, decimal? rewardPrice = null, DateTime? valuedAt = null) =>
        new()
        {
            Status = status,
            Pool = new Pool { Token0 = Tok(s0), Token1 = Tok(s1) },
            CurrentAmount0 = a0,
            CurrentAmount1 = a1,
            Price0Usd = p0,
            Price1Usd = p1,
            Uncollected0 = unc0,
            Uncollected1 = unc1,
            PendingReward = pending,
            RewardTokenAddress = Aero,
            RewardTokenSymbol = "AERO",
            RewardPriceUsd = rewardPrice,
            ValuedAt = valuedAt ?? T0.AddDays(30),
        };

    private static PositionEvent Claim(int day, decimal amount, decimal? price, string address = Aero) =>
        new()
        {
            Type = PositionEventType.RewardClaim,
            Timestamp = T0.AddDays(day),
            RewardAmount = amount,
            RewardPriceUsd = price,
            RewardTokenAddress = address,
            RewardTokenSymbol = "AERO",
        };

    [Theory]
    [InlineData("USDC", AssetClass.Stablecoin)]
    [InlineData("usdbc", AssetClass.Stablecoin)]
    [InlineData("WETH", AssetClass.Ether)]
    [InlineData("wstETH", AssetClass.Ether)]
    [InlineData("cbBTC", AssetClass.Bitcoin)]
    [InlineData("AERO", AssetClass.Other)]
    [InlineData("", AssetClass.Other)]
    [InlineData(null, AssetClass.Other)]
    public void Classifies_tokens_by_symbol(string? symbol, AssetClass expected) =>
        Assert.Equal(expected, AssetClassifier.Classify(symbol));

    [Fact]
    public void Exposure_sums_what_positions_hold_per_token_across_positions()
    {
        var e = PortfolioExposureCalculator.Calculate([
            Pos("WETH", "USDC", 1, 2000, 2000, 1),
            Pos("WETH", "cbBTC", 0.5m, 0.02m, 2000, 50_000),
        ]);

        var weth = e.Tokens.Single(t => t.Symbol == "WETH");
        Assert.Equal(1.5m, weth.Amount);
        Assert.Equal(3000, weth.ValueUsd);
        Assert.Equal(6000, e.TotalUsd);
        Assert.Equal("WETH", e.Tokens[0].Symbol); // largest first
        Assert.Equal(3000, e.Classes.Single(c => c.AssetClass == AssetClass.Ether).ValueUsd);
        Assert.Equal(2000, e.Classes.Single(c => c.AssetClass == AssetClass.Stablecoin).ValueUsd);
        Assert.Equal(1000, e.Classes.Single(c => c.AssetClass == AssetClass.Bitcoin).ValueUsd);
    }

    [Fact]
    public void Exposure_keeps_unclaimed_fees_and_rewards_separate_from_principal()
    {
        var e = PortfolioExposureCalculator.Calculate([
            Pos("WETH", "USDC", 1, 2000, 2000, 1, unc0: 0.01m, unc1: 20, pending: 100, rewardPrice: 0.5m),
        ]);

        var weth = e.Tokens.Single(t => t.Symbol == "WETH");
        Assert.Equal(2000, weth.ValueUsd);
        Assert.Equal(20, weth.UnclaimedUsd);
        Assert.Equal(20, e.Tokens.Single(t => t.Symbol == "USDC").UnclaimedUsd);
        var aero = e.Tokens.Single(t => t.Symbol == "AERO");
        Assert.Equal(0, aero.ValueUsd);
        Assert.Equal(50, aero.UnclaimedUsd);
        Assert.Equal(2000 + 2000 + 20 + 20 + 50, e.TotalUsd);
    }

    [Fact]
    public void Exposure_ignores_closed_positions_and_flags_missing_prices()
    {
        var e = PortfolioExposureCalculator.Calculate([
            Pos("WETH", "USDC", 1, 2000, 2000, 1, status: PositionStatus.Closed),
            Pos("WETH", "AERO", 1, 500, 2000, null),
        ]);

        Assert.True(e.HasMissingPrices);
        Assert.Equal(2000, e.TotalUsd); // only the priced WETH of the open position
        Assert.DoesNotContain(e.Tokens, t => t.Symbol == "USDC");
    }

    [Fact]
    public void Exposure_of_nothing_is_empty()
    {
        var e = PortfolioExposureCalculator.Calculate([]);
        Assert.Equal(0, e.TotalUsd);
        Assert.Empty(e.Tokens);
        Assert.False(e.HasMissingPrices);
    }

    [Fact]
    public void Rewards_compare_value_at_claim_with_value_now()
    {
        var p = Pos("WETH", "USDC", 1, 1, 1, 1, rewardPrice: 0.5m);
        p.Events.AddRange([Claim(1, 100, 1m), Claim(2, 100, 0.8m)]);

        var r = Assert.Single(RewardPnlCalculator.Calculate([p]));

        Assert.Equal(2, r.ClaimCount);
        Assert.Equal(200, r.ClaimedAmount);
        Assert.Equal(180, r.ClaimedUsd);
        Assert.Equal(0.9m, r.AverageClaimPriceUsd);
        Assert.Equal(0.5m, r.CurrentPriceUsd);
        Assert.Equal(100, r.ClaimedValueNowUsd);
        Assert.Equal(-80, r.ChangeSinceClaimUsd);
        Assert.Equal(-80m / 180 * 100, r.ChangeSinceClaimPercent);
        Assert.False(r.HasEstimatedPrices);
    }

    [Fact]
    public void Rewards_value_unpriced_claims_at_next_known_price_and_flag_it()
    {
        var p = Pos("WETH", "USDC", 1, 1, 1, 1, rewardPrice: 0.5m);
        p.Events.AddRange([Claim(1, 100, null), Claim(2, 100, 0.8m)]);

        var r = Assert.Single(RewardPnlCalculator.Calculate([p]));

        Assert.Equal(160, r.ClaimedUsd);
        Assert.True(r.HasEstimatedPrices);
        Assert.False(r.HasMissingPrices);
    }

    [Fact]
    public void Rewards_without_any_price_are_counted_but_not_valued()
    {
        var p = Pos("WETH", "USDC", 1, 1, 1, 1, rewardPrice: null);
        p.Events.Add(Claim(1, 100, null));

        var r = Assert.Single(RewardPnlCalculator.Calculate([p]));

        Assert.Equal(100, r.ClaimedAmount);
        Assert.Equal(0, r.ClaimedUsd);
        Assert.True(r.HasMissingPrices);
        Assert.Null(r.ChangeSinceClaimUsd);
        Assert.Null(r.CurrentPriceUsd);
    }

    [Fact]
    public void Rewards_use_the_most_recently_valued_price_and_sum_pending_across_positions()
    {
        var stale = Pos("WETH", "USDC", 1, 1, 1, 1, pending: 10, rewardPrice: 0.4m, valuedAt: T0.AddDays(10));
        var fresh = Pos("WETH", "USDC", 1, 1, 1, 1, pending: 30, rewardPrice: 0.6m, valuedAt: T0.AddDays(20));
        fresh.Events.Add(Claim(1, 100, 1m));

        var r = Assert.Single(RewardPnlCalculator.Calculate([stale, fresh]));

        Assert.Equal(0.6m, r.CurrentPriceUsd);
        Assert.Equal(40, r.PendingAmount);
        Assert.Equal(24, r.PendingUsd);
    }

    [Fact]
    public void Rewards_keep_different_tokens_apart_and_ignore_closed_pending()
    {
        var a = Pos("WETH", "USDC", 1, 1, 1, 1, rewardPrice: 2m);
        a.Events.Add(Claim(1, 10, 1m));
        var b = Pos("WETH", "USDC", 1, 1, 1, 1, status: PositionStatus.Closed, pending: 99, rewardPrice: 2m);
        b.Events.Add(Claim(1, 5, 1m, address: "0xother"));

        var r = RewardPnlCalculator.Calculate([a, b]);

        Assert.Equal(2, r.Count);
        Assert.Equal(0, r.Single(x => x.Address == Aero).PendingAmount);
        Assert.Equal(5, r.Single(x => x.Address == "0xother").ClaimedAmount);
    }
}
