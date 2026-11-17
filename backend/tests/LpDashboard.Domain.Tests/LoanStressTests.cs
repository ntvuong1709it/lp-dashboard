using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class LoanStressTests
{
    private static readonly MarketOptions O = new();

    private static LoanStress Ltv(decimal ltv, decimal debt, decimal? margin = 70m, decimal liq = 78m) =>
        LoanStressCalculator.Compute("Ltv", ltv, margin, liq, 20_000m, debt, O)!;

    [Fact]
    public void Thin_loan_walks_from_ok_to_margin_call()
    {
        var s = Ltv(52m, 10_400m);

        Assert.Equal(BufferLevel.Thin, s.Buffer);
        Assert.Equal(33.33m, s.DropToLiquidationPercent, 2);
        Assert.Equal([10, 20, 30], s.Rows.Select(r => r.FallPercent));
        Assert.Equal([57.78m, 65.00m, 74.29m], s.Rows.Select(r => Math.Round(r.Value, 2)));
        Assert.Equal([StressStatus.Ok, StressStatus.Watch, StressStatus.MarginCall], s.Rows.Select(r => r.Status));
        Assert.Equal(0m, s.Rows[0].AddCollateralUsd);
        Assert.Equal(0m, s.Rows[1].AddCollateralUsd);
        Assert.Equal(857.14m, s.Rows[2].AddCollateralUsd, 2);
    }

    [Fact]
    public void Comfortable_loan_needs_nothing()
    {
        var s = Ltv(35m, 7_000m);

        Assert.Equal(BufferLevel.Comfortable, s.Buffer);
        Assert.All(s.Rows, r => Assert.Equal(StressStatus.Ok, r.Status));
        Assert.All(s.Rows, r => Assert.Equal(0m, r.AddCollateralUsd));
    }

    [Fact]
    public void Tight_loan_reaches_liquidation()
    {
        var s = Ltv(66m, 13_200m);

        Assert.Equal(BufferLevel.Tight, s.Buffer);
        Assert.Equal([StressStatus.MarginCall, StressStatus.Liquidation, StressStatus.Liquidation], s.Rows.Select(r => r.Status));
        Assert.Equal([73.33m, 82.50m, 94.29m], s.Rows.Select(r => Math.Round(r.Value, 2)));
        Assert.Equal([857.14m, 2_857.14m, 4_857.14m], s.Rows.Select(r => Math.Round(r.AddCollateralUsd, 2)));
    }

    [Fact]
    public void Without_a_margin_call_level_liquidation_is_the_target()
    {
        var s = Ltv(66m, 13_200m, margin: null);

        Assert.Equal([StressStatus.Watch, StressStatus.Liquidation, StressStatus.Liquidation], s.Rows.Select(r => r.Status));
        Assert.Equal([0m, 923.08m, 2_923.08m], s.Rows.Select(r => Math.Round(r.AddCollateralUsd, 2)));
    }

    [Fact]
    public void Health_factor_loan()
    {
        var s = LoanStressCalculator.Compute("HealthFactor", 1.5m, null, null, 20_000m, 10_000m, O)!;

        Assert.Equal(33.33m, s.DropToLiquidationPercent, 2);
        Assert.Equal([1.35m, 1.20m, 1.05m], s.Rows.Select(r => Math.Round(r.Value, 2)));
        Assert.Equal([StressStatus.Ok, StressStatus.Watch, StressStatus.Watch], s.Rows.Select(r => r.Status));
        Assert.Equal([0m, 0m, 2_000m], s.Rows.Select(r => Math.Round(r.AddCollateralUsd, 2)));
    }

    [Fact]
    public void Health_factor_below_one_after_a_fall_is_liquidation()
    {
        var s = LoanStressCalculator.Compute("HealthFactor", 1.2m, null, null, 20_000m, 10_000m, O)!;

        Assert.Equal(StressStatus.Liquidation, s.Rows[2].Status); // 1.2 x 0.7 = 0.84
    }

    [Fact]
    public void No_usable_loan_gives_null()
    {
        Assert.Null(LoanStressCalculator.Compute("None", 1.5m, 70m, 78m, 20_000m, 10_000m, O));
        Assert.Null(LoanStressCalculator.Compute("Ltv", null, 70m, 78m, 20_000m, 10_000m, O));
        Assert.Null(LoanStressCalculator.Compute("Ltv", 52m, 70m, 78m, null, 10_000m, O));
        Assert.Null(LoanStressCalculator.Compute("Ltv", 52m, 70m, 78m, 0m, 10_000m, O));
    }

    [Fact]
    public void Ltv_loan_without_a_liquidation_level_gives_null() =>
        Assert.Null(LoanStressCalculator.Compute("Ltv", 52m, 70m, null, 20_000m, 10_400m, O));
}
