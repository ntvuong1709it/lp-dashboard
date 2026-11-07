using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class LoanCostTests
{
    [Fact]
    public void SixThousandAtNinePointTwoPercent_CostsAboutOneFiftyOneADay()
    {
        var cost = LoanCostCalculator.Cost(6000m, 9.2m);

        Assert.Equal(552m, cost.PerYear);
        Assert.Equal(1.51m, Math.Round(cost.PerDay, 2));
        Assert.Equal(10.59m, Math.Round(cost.PerWeek, 2));
        Assert.Equal(46.00m, Math.Round(cost.PerMonth, 2));
    }

    [Fact]
    public void NoDebtOrNoRate_CostsNothing()
    {
        Assert.Equal(LoanCost.Zero, LoanCostCalculator.Cost(0, 5));
        Assert.Equal(LoanCost.Zero, LoanCostCalculator.Cost(1000, 0));
    }

    [Fact]
    public void Sum_AddsEveryPeriod()
    {
        var total = LoanCostCalculator.Sum([LoanCostCalculator.Cost(14200m, 5.8m), LoanCostCalculator.Cost(6000m, 9.2m)]);

        Assert.Equal(Math.Round((14200m * 0.058m + 552m) / 365m, 4), Math.Round(total.PerDay, 4));
        Assert.Equal(total.PerYear / 12m, total.PerMonth);
    }

    [Fact]
    public void IncomeAtTheSamePace_UsesTheSamePeriods()
    {
        var income = LoanCost.FromPerDay(6m);

        Assert.Equal(42m, income.PerWeek);
        Assert.Equal(2190m, income.PerYear);
        Assert.Equal(182.5m, income.PerMonth);
    }
}

public class ManualLoanAccrualTests
{
    private static readonly DateOnly D1 = new(2026, 1, 1);

    [Fact]
    public void RateChange_AppliesFromItsDateOnly()
    {
        var terms = new[] { new TermRow(D1, 3650m, 10m), new TermRow(D1.AddDays(10), 3650m, 20m) };

        var days = ManualLoanAccrual.Daily(terms, [], null, D1, D1.AddDays(12));

        Assert.Equal(1m, days[9].InterestTokens);   // 3650 * 10% / 365
        Assert.Equal(2m, days[10].InterestTokens);  // 3650 * 20% / 365
        Assert.Equal(20m, days[10].AprPercent);
    }

    [Fact]
    public void Debt_IsPrincipalPlusInterestSinceTheRow_AndNewRowRestatesIt()
    {
        var terms = new[] { new TermRow(D1, 3650m, 10m), new TermRow(D1.AddDays(5), 3700m, 10m) };

        var days = ManualLoanAccrual.Daily(terms, [], null, D1, D1.AddDays(6));

        Assert.Equal(3650m, days[0].DebtTokens);
        Assert.Equal(3654m, days[4].DebtTokens);
        Assert.Equal(3700m, days[5].DebtTokens);
        Assert.Equal(3701.0136986301369863m, Math.Round(days[6].DebtTokens, 16));
    }

    [Fact]
    public void BeforeTheFirstRowAndAfterClosing_NothingAccrues()
    {
        var terms = new[] { new TermRow(D1.AddDays(2), 1000m, 10m) };

        var days = ManualLoanAccrual.Daily(terms, [], D1.AddDays(4), D1, D1.AddDays(6));

        Assert.False(days[0].Active);
        Assert.True(days[2].Active);
        Assert.True(days[4].Active);
        Assert.False(days[5].Active);
        Assert.Equal(0m, days[5].DebtTokens);
    }

    [Fact]
    public void Collateral_FollowsItsOwnHistory()
    {
        var terms = new[] { new TermRow(D1, 1000m, 5m) };
        var collateral = new[] { new AmountRow(D1, 0.138m), new AmountRow(D1.AddDays(3), 0.15m) };

        var days = ManualLoanAccrual.Daily(terms, collateral, null, D1, D1.AddDays(4));

        Assert.Equal(0.138m, days[2].CollateralTokens);
        Assert.Equal(0.15m, days[3].CollateralTokens);
    }

    [Fact]
    public void Current_IgnoresFutureRowsAndClosedLoans()
    {
        var terms = new[] { new TermRow(D1, 1000m, 5m), new TermRow(D1.AddDays(30), 2000m, 8m) };

        Assert.Equal(5m, ManualLoanAccrual.Current(terms, null, D1.AddDays(10))!.Value.AprPercent);
        Assert.Equal(8m, ManualLoanAccrual.Current(terms, null, D1.AddDays(31))!.Value.AprPercent);
        Assert.Null(ManualLoanAccrual.Current(terms, D1.AddDays(5), D1.AddDays(10)));
        Assert.Null(ManualLoanAccrual.Current(terms, null, D1.AddDays(-1)));
    }
}

public class CollateralTrendTests
{
    private static readonly DateOnly D1 = new(2026, 1, 1);

    private static IReadOnlyList<BalanceDay> Days(params (decimal Tokens, decimal Price, decimal Accrual)[] rows) =>
        rows.Select((r, i) => new BalanceDay(D1.AddDays(i), r.Tokens, r.Price, r.Accrual)).ToList();

    [Fact]
    public void Split_PartsAddUpToTheChangeExactly()
    {
        // Collateral: 10 tokens, price moves, a deposit of 2 on day 2, 0.1 yield a day. Debt: 1000 + interest + a borrow of 50 on day 3.
        var collateral = Days((10m, 100m, 0m), (10.1m, 110m, .1m), (12.2m, 105m, .1m), (12.3m, 90m, .1m), (12.4m, 95m, .1m));
        var debt = Days((1000m, 1m, 0m), (1001m, 1m, 1m), (1002m, 1m, 1m), (1053m, 1m, 1m), (1054m, 1m, 1m));
        var leg = new TrendLeg("test", collateral, debt);
        var to = D1.AddDays(4);

        var split = CollateralTrendCalculator.Split([leg], D1, to);

        Assert.Equal(1000m, split.CollateralStartUsd);
        Assert.Equal(12.4m * 95m, split.CollateralEndUsd);
        Assert.Equal(
            split.CollateralEndUsd - split.CollateralStartUsd,
            split.PriceMovesUsd + split.DepositsUsd + split.SupplyYieldUsd);
        Assert.Equal(
            split.DebtEndUsd - split.DebtStartUsd,
            split.InterestUsd + split.BorrowedUsd + split.DebtPriceMovesUsd);
        Assert.Equal(4m, split.InterestUsd);
        Assert.Equal(50m, split.BorrowedUsd);
        Assert.Equal(0m, split.DebtPriceMovesUsd);
    }

    [Fact]
    public void Split_ValuesADepositAtThePriceOfItsDay()
    {
        var collateral = Days((10m, 100m, 0m), (12m, 120m, 0m));
        var debt = Days((500m, 1m, 0m), (500m, 1m, 0m));

        var split = CollateralTrendCalculator.Split([new TrendLeg("t", collateral, debt)], D1, D1.AddDays(1));

        Assert.Equal(240m, split.DepositsUsd);   // 2 tokens * 120
        Assert.Equal(200m, split.PriceMovesUsd); // 10 tokens * (120 - 100)
    }

    [Fact]
    public void Series_RatioAndLtvFollowTheTwoSides()
    {
        var leg = new TrendLeg("t", Days((10m, 100m, 0m), (10m, 80m, 0m)), Days((400m, 1m, 0m), (400m, 1m, 0m)));

        var points = CollateralTrendCalculator.Series([leg], D1, D1.AddDays(1));

        Assert.Equal(2.5m, points[0].Ratio);
        Assert.Equal(40m, points[0].LtvPercent);
        Assert.Equal(2m, points[1].Ratio);
        Assert.Equal(50m, points[1].LtvPercent);
    }

    [Fact]
    public void Series_SumsSeveralLegs()
    {
        var a = new TrendLeg("a", Days((1m, 100m, 0m)), Days((40m, 1m, 0m)));
        var b = new TrendLeg("b", Days((2m, 50m, 0m)), Days((60m, 1m, 0m)));

        var point = CollateralTrendCalculator.Series([a, b], D1, D1).Single();

        Assert.Equal(200m, point.CollateralUsd);
        Assert.Equal(100m, point.DebtUsd);
    }

    [Fact]
    public void Series_WithoutDebt_HasNoRatio()
    {
        var leg = new TrendLeg("t", Days((1m, 100m, 0m)), Days((0m, 1m, 0m)));

        Assert.Null(CollateralTrendCalculator.Series([leg], D1, D1).Single().Ratio);
    }

    [Fact]
    public void Events_FlagDepositsAndBorrows_NotPriceMovesOrInterest()
    {
        var collateral = Days((10m, 100m, 0m), (10m, 120m, 0m), (12m, 120m, 0m));
        var debt = Days((500m, 1m, 0m), (501m, 1m, 1m), (552m, 1m, 1m));

        var events = CollateralTrendCalculator.Events([new TrendLeg("t", collateral, debt)], D1, D1.AddDays(2));

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Kind == TrendEventKind.CollateralAdded && e.Usd == 240m);
        Assert.Contains(events, e => e.Kind == TrendEventKind.Borrowed && e.Usd == 50m);
    }

    [Fact]
    public void Events_FlagWithdrawalsAndRepayments()
    {
        var collateral = Days((10m, 100m, 0m), (8m, 100m, 0m));
        var debt = Days((500m, 1m, 0m), (400m, 1m, 0m));

        var events = CollateralTrendCalculator.Events([new TrendLeg("t", collateral, debt)], D1, D1.AddDays(1));

        Assert.Contains(events, e => e.Kind == TrendEventKind.CollateralRemoved && e.Usd == -200m);
        Assert.Contains(events, e => e.Kind == TrendEventKind.Repaid && e.Usd == -100m);
    }

    [Fact]
    public void HealthFactorTarget_GivesRepayOrCollateral()
    {
        // Debt 14,200, collateral 32,800, threshold 0.753 (health factor 1.74), target 2.0.
        var r = CollateralTrendCalculator.ForHealthFactor(32800m, 14200m, 0.753m, 2m)!;

        Assert.Equal(1850.8m, Math.Round(r.RepayUsd, 1));
        Assert.Equal(4915.8m, Math.Round(r.AddCollateralUsd, 1));
    }

    [Fact]
    public void Targets_AlreadyMet_NeedNothing()
    {
        Assert.Equal(new Repayment(0, 0), CollateralTrendCalculator.ForHealthFactor(32800m, 14200m, 0.753m, 1.5m));
        Assert.Equal(new Repayment(0, 0), CollateralTrendCalculator.ForLtv(10000m, 4000m, 60m));
        Assert.Null(CollateralTrendCalculator.ForLtv(10000m, 0m, 60m));
    }

    [Fact]
    public void LtvTarget_GivesRepayOrCollateral()
    {
        // Debt 6,000 against 8,450 collateral (LTV 71%), target 60%.
        var r = CollateralTrendCalculator.ForLtv(8450m, 6000m, 60m)!;

        Assert.Equal(930m, r.RepayUsd);
        Assert.Equal(1550m, Math.Round(r.AddCollateralUsd, 0));
    }
}

public class BorrowCoverageTests
{
    private static Coverage Cov(decimal fees, decimal rewards, decimal supply, decimal interest, decimal gas = 0) =>
        new(fees, rewards, supply, interest, gas);

    [Fact]
    public void RatioAndNetCarry_UseIncomeInterestAndGas()
    {
        var c = Cov(100, 50, 10, 80, 12);

        Assert.Equal(160m, c.LpIncomeUsd);
        Assert.Equal(2m, c.Ratio);
        Assert.Equal(68m, c.NetCarryUsd);
        Assert.Equal(CoverageStatus.Covered, c.Status);
    }

    [Theory]
    [InlineData(125, 100, CoverageStatus.Covered)]
    [InlineData(124, 100, CoverageStatus.Thin)]
    [InlineData(100, 100, CoverageStatus.Thin)]
    [InlineData(99, 100, CoverageStatus.Shortfall)]
    public void Status_BoundariesAre1_25AndOne(decimal income, decimal interest, CoverageStatus expected) =>
        Assert.Equal(expected, Cov(income, 0, 0, interest).Status);

    [Fact]
    public void NoInterest_HasNoRatio()
    {
        var c = Cov(10, 0, 0, 0);

        Assert.Null(c.Ratio);
        Assert.Equal(CoverageStatus.NoDebt, c.Status);
    }

    [Theory]
    [InlineData(1.49, RiskLevel.Warn)]
    [InlineData(1.5, RiskLevel.Ok)]
    [InlineData(1.19, RiskLevel.Critical)]
    [InlineData(1.2, RiskLevel.Warn)]
    public void HealthFactorLevels(decimal hf, RiskLevel expected) =>
        Assert.Equal(expected, BorrowCoverageCalculator.HealthFactorLevel(hf));

    [Theory]
    [InlineData(60, RiskLevel.Ok)]
    [InlineData(71, RiskLevel.Warn)]
    [InlineData(77, RiskLevel.Critical)]
    public void LtvLevels_AreRelativeToLiquidationLtv(decimal ltv, RiskLevel expected) =>
        Assert.Equal(expected, BorrowCoverageCalculator.LtvLevel(ltv, 80m));

    [Fact]
    public void DropToLiquidation()
    {
        Assert.Equal(11.25m, BorrowCoverageCalculator.LtvDropToLiquidationPercent(71m, 80m));
        Assert.Equal(42.53m, Math.Round(BorrowCoverageCalculator.HealthFactorDropToLiquidationPercent(1.74m), 2));
        Assert.Equal(0m, BorrowCoverageCalculator.LtvDropToLiquidationPercent(90m, 80m));
    }

    [Fact]
    public void Repay_SurplusGoesToTheHighestRateLoan()
    {
        var c = Cov(200, 0, 0, 100);
        var loans = new[] { new LoanRate("Aave", 5.8m), new LoanRate("Binance", 9.2m) };

        var s = BorrowCoverageCalculator.Repay(c, 30, loans);

        Assert.Equal(RepayMode.Surplus, s.Mode);
        Assert.Equal("Binance", s.LoanLabel);
        Assert.Equal(100m, s.AmountUsd);
        Assert.Equal(9.2m, s.SavingPerYearUsd);
    }

    [Fact]
    public void Repay_ShortfallSaysHowMuchDebtToCut()
    {
        var c = Cov(50, 0, 0, 100);
        var loans = new[] { new LoanRate("Binance", 10m) };

        var s = BorrowCoverageCalculator.Repay(c, 30, loans);

        Assert.Equal(RepayMode.Shortfall, s.Mode);
        // (100 - 50) / 30 * 365 = 608.33 a year of interest to remove; at 10% that is 6,083.33 of debt.
        Assert.Equal(608.33m, Math.Round(s.SavingPerYearUsd, 2));
        Assert.Equal(6083.33m, Math.Round(s.AmountUsd, 2));
    }

    [Fact]
    public void Repay_WithoutLoansOrInterest_SuggestsNothing()
    {
        Assert.Equal(RepayMode.None, BorrowCoverageCalculator.Repay(Cov(10, 0, 0, 0), 30, [new LoanRate("x", 5)]).Mode);
        Assert.Equal(RepayMode.None, BorrowCoverageCalculator.Repay(Cov(10, 0, 0, 5), 30, []).Mode);
    }
}
