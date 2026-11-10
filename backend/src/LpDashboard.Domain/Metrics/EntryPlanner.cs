using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Metrics;

public enum SwapSide
{
    None,
    SellToken0,
    SellToken1
}

/// <summary>Which token a budget in USD starts out as.</summary>
public enum PayingWith
{
    Token0,
    Token1,
    /// <summary>Half the budget in each token.</summary>
    Split
}

/// <param name="Pool">Pool price, decimal-adjusted: token1 per token0. Drives the ratio and the swap rate.</param>
/// <param name="Usd0">USD reference price of token0; only values the result.</param>
/// <param name="Usd1">USD reference price of token1.</param>
public sealed record PoolPrices(double Pool, double Usd0, double Usd1);

/// <summary>A tick range snapped to the pool's spacing, with the prices and distances the snapped ticks actually give.</summary>
/// <param name="LowerPercent">Signed distance of the lower price from the pool price, in percent (negative below).</param>
/// <param name="UpperPercent">Signed distance of the upper price from the pool price, in percent.</param>
public sealed record SnappedRange(int TickLower, int TickUpper, double PriceLower, double PriceUpper, double LowerPercent, double UpperPercent)
{
    public int TickWidth => TickUpper - TickLower;
}

/// <param name="SwapAmountIn">Amount of the sold token (see <see cref="Side"/>).</param>
/// <param name="SwapProceeds">Amount of the other token received after the swap fee and slippage.</param>
/// <param name="SwapCostUsd">Swap fee plus slippage on the swapped notional, in USD.</param>
/// <param name="EntryCostUsd">Swap cost plus gas.</param>
/// <param name="Amount0">Token amounts to deposit after the swap (human units).</param>
/// <param name="ValueUsd">Value of the deposit at the USD reference prices, after swap costs.</param>
/// <param name="StartValueUsd">Value of what was held before the swap.</param>
/// <param name="Liquidity">Human liquidity (see <see cref="PositionMath.AmountsPerLiquidity"/>).</param>
/// <param name="Deposit0">Amount0 rounded down to the token's decimals (capped at <see cref="EntryPlanner.MaxDepositDecimals"/>), for typing into a deposit screen.</param>
/// <param name="DustUsd">Value lost to that rounding.</param>
/// <param name="SwapShareOfTvl">Swap notional as a share of the pool's TVL; null when the TVL is unknown.</param>
public sealed record EntryPlan(
    SwapSide Side,
    double SwapAmountIn,
    double SwapProceeds,
    double SwapNotionalUsd,
    double SwapCostUsd,
    double GasUsd,
    double EntryCostUsd,
    double Amount0,
    double Amount1,
    double Amount0Usd,
    double Amount1Usd,
    double ValueUsd,
    double StartValueUsd,
    double Token0Share,
    double Token1Share,
    double Liquidity,
    double Deposit0,
    double Deposit1,
    double DustUsd,
    double? SwapShareOfTvl,
    bool SwapShareWarning);

/// <summary>
/// Works out what to hold to open a concentrated-liquidity range: the swap that turns what you have into the token mix
/// the range needs at the current price, and what that costs. Pure maths, no I/O. The same solver backs the rebalance
/// simulator (docs/rebalance-simulator.md, section 1).
///
/// Prices are decimal-adjusted token1 per token0; amounts are human units. The pool price decides the ratio and the swap
/// rate, the USD reference prices only value the result.
/// </summary>
public static class EntryPlanner
{
    public const double MinLowerPercent = 0.1;
    public const double MaxLowerPercent = 99;
    public const double MinUpperPercent = 0.1;
    public const double MaxUpperPercent = 500;

    /// <summary>A swap above this share of the pool's TVL moves the price more than the flat cost assumes.</summary>
    public const double SwapShareWarningThreshold = 0.001;

    /// <summary>Deposit amounts are shown with at most this many decimals, however many the token has.</summary>
    public const int MaxDepositDecimals = 8;

    /// <summary>
    /// Range from percentages around the pool price: the lower bound that far below, the upper that far above (each clamped
    /// to the allowed percent range), snapped to the nearest tick spacing. The result is what the amounts are computed from.
    /// </summary>
    public static SnappedRange SnapRange(double poolPrice, double lowerPercent, double upperPercent, int tickSpacing, int decimals0, int decimals1)
    {
        RequirePositive(poolPrice, nameof(poolPrice));
        var lo = Math.Clamp(lowerPercent, MinLowerPercent, MaxLowerPercent);
        var hi = Math.Clamp(upperPercent, MinUpperPercent, MaxUpperPercent);

        var lower = PositionMath.SnapTick(PositionMath.PriceToTickExact(poolPrice * (1 - lo / 100), decimals0, decimals1), tickSpacing);
        var upper = PositionMath.SnapTick(PositionMath.PriceToTickExact(poolPrice * (1 + hi / 100), decimals0, decimals1), tickSpacing);
        if (upper <= lower) upper = lower + tickSpacing; // a range narrower than one spacing still needs one step of width
        return Describe(poolPrice, lower, upper, decimals0, decimals1);
    }

    /// <summary>Range from explicit ticks (snapped to the spacing). The price may sit outside it: the plan is then one token only.</summary>
    public static SnappedRange RangeFromTicks(double poolPrice, int tickLower, int tickUpper, int tickSpacing, int decimals0, int decimals1)
    {
        RequirePositive(poolPrice, nameof(poolPrice));
        var lower = PositionMath.SnapTick(tickLower, tickSpacing);
        var upper = PositionMath.SnapTick(tickUpper, tickSpacing);
        if (upper <= lower) throw new ArgumentException("The upper tick must be above the lower tick.", nameof(tickUpper));
        return Describe(poolPrice, lower, upper, decimals0, decimals1);
    }

    private static SnappedRange Describe(double poolPrice, int lower, int upper, int decimals0, int decimals1)
    {
        var pa = PositionMath.TickToPrice(lower, decimals0, decimals1);
        var pb = PositionMath.TickToPrice(upper, decimals0, decimals1);
        return new SnappedRange(lower, upper, pa, pb, (pa / poolPrice - 1) * 100, (pb / poolPrice - 1) * 100);
    }

    /// <summary>What a USD budget is held as before the swap.</summary>
    public static (double Amount0, double Amount1) HoldingsFromBudget(double budgetUsd, PayingWith paying, PoolPrices prices)
    {
        RequirePrices(prices);
        var b = Math.Max(0, budgetUsd);
        return paying switch
        {
            PayingWith.Token0 => (b / prices.Usd0, 0),
            PayingWith.Token1 => (0, b / prices.Usd1),
            _ => (b / 2 / prices.Usd0, b / 2 / prices.Usd1)
        };
    }

    /// <param name="holding0">Token0 held before the swap.</param>
    /// <param name="holding1">Token1 held before the swap.</param>
    /// <param name="swapCostFraction">Swap fee plus slippage as a fraction of the amount sold (0.001 = 0.10%).</param>
    /// <param name="gasUsd">Gas for the swap and the mint, in USD.</param>
    /// <param name="tvlUsd">Pool TVL, to flag a swap too large for a flat cost assumption.</param>
    public static EntryPlan Plan(
        double holding0, double holding1, PoolPrices prices, SnappedRange range, double swapCostFraction,
        double gasUsd = 0, double? tvlUsd = null, int decimals0 = 18, int decimals1 = 18)
    {
        RequirePrices(prices);
        if (holding0 < 0 || holding1 < 0) throw new ArgumentOutOfRangeException(nameof(holding0), "Holdings cannot be negative.");
        if (swapCostFraction < 0 || swapCostFraction >= 1) throw new ArgumentOutOfRangeException(nameof(swapCostFraction));
        if (range.PriceUpper <= range.PriceLower) throw new ArgumentException("The range has no width.", nameof(range));

        double p = prices.Pool, c = swapCostFraction, w0 = holding0, w1 = holding1;
        var (x, y) = PositionMath.AmountsPerLiquidity(p, range.PriceLower, range.PriceUpper);

        // After selling s of token0 the holdings are (w0 - s, w1 + s*P*(1-c)); they must be in the ratio y/x:
        // (w1 + s*P*(1-c)) * x = (w0 - s) * y  =>  s = (w0*y - w1*x) / (y + x*P*(1-c)).
        // A negative s means token1 is the one to sell; the mirror image is solved the same way.
        var s = (w0 * y - w1 * x) / (y + x * p * (1 - c));

        // Float noise on holdings that already match the range must not show up as a swap: anything under a billionth of
        // the holdings' value counts as no swap.
        var noise = 1e-9 * (w0 * p + w1);

        SwapSide side;
        double sold, got, a0, a1, swapUsd;
        if (s * p > noise)
        {
            sold = Math.Min(s, w0);
            got = sold * p * (1 - c);
            a0 = w0 - sold;
            a1 = w1 + got;
            swapUsd = sold * prices.Usd0;
            side = SwapSide.SellToken0;
        }
        else
        {
            var t = (w1 * x - w0 * y) / (x + y * (1 - c) / p);
            if (t > noise)
            {
                sold = Math.Min(t, w1);
                got = sold * (1 - c) / p;
                a0 = w0 + got;
                a1 = w1 - sold;
                swapUsd = sold * prices.Usd1;
                side = SwapSide.SellToken1;
            }
            else
            {
                sold = got = swapUsd = 0;
                a0 = w0;
                a1 = w1;
                side = SwapSide.None;
            }
        }

        var swapCost = swapUsd * c;
        var usd0 = a0 * prices.Usd0;
        var usd1 = a1 * prices.Usd1;
        var value = usd0 + usd1;
        var liquidity = x > 0 && a0 > 0 ? a0 / x : y > 0 ? a1 / y : 0;

        var dec0 = Math.Min(decimals0, MaxDepositDecimals);
        var dec1 = Math.Min(decimals1, MaxDepositDecimals);
        var dep0 = RoundDown(a0, dec0);
        var dep1 = RoundDown(a1, dec1);
        var dust = (a0 - dep0) * prices.Usd0 + (a1 - dep1) * prices.Usd1;

        double? share = tvlUsd is > 0 ? swapUsd / tvlUsd.Value : null;
        var share0 = value > 0 ? usd0 / value : 0;

        return new EntryPlan(
            side, sold, got, swapUsd, swapCost, gasUsd, swapCost + gasUsd,
            a0, a1, usd0, usd1, value, w0 * prices.Usd0 + w1 * prices.Usd1,
            share0, value > 0 ? 1 - share0 : 0,
            liquidity, dep0, dep1, dust, share, share > SwapShareWarningThreshold);
    }

    private static double RoundDown(double value, int decimals)
    {
        var f = Math.Pow(10, decimals);
        return Math.Floor(value * f) / f;
    }

    private static void RequirePositive(double v, string name)
    {
        if (!(v > 0) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(name, "Must be a positive number.");
    }

    private static void RequirePrices(PoolPrices prices)
    {
        RequirePositive(prices.Pool, nameof(prices.Pool));
        RequirePositive(prices.Usd0, nameof(prices.Usd0));
        RequirePositive(prices.Usd1, nameof(prices.Usd1));
    }
}
