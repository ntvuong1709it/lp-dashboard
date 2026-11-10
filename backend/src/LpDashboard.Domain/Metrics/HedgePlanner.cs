using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Metrics;

public enum HedgeInstrument
{
    /// <summary>A short perpetual future. Positive funding is paid to the short, so it earns carry.</summary>
    PerpShort,

    /// <summary>Borrow the volatile token and sell it. A positive borrow rate is interest you pay.</summary>
    BorrowAndSell
}

/// <summary>Which legs of the pool carry price risk and get a short.</summary>
public enum HedgedLegs
{
    /// <summary>Both tokens are stable: nothing to hedge.</summary>
    None,
    Token0,
    Token1,
    /// <summary>Both are volatile (WETH/cbBTC): one short per leg.</summary>
    Both
}

/// <param name="Ratio">Share of the position's delta that is shorted, 0 to 1. 0 means no hedge.</param>
/// <param name="AnnualRate">
/// Per year, as a fraction. Perp: the funding rate (positive: longs pay shorts, so the short earns it). Borrow and sell:
/// the borrow rate (positive: you pay it).
/// </param>
/// <param name="IncomeApr">Fee (or emission) APR on the position, as a fraction, earned while the price is in range.</param>
/// <param name="TradingFee">Fee to open and the same to close the short, each a fraction of the notional.</param>
/// <param name="MaintenanceMargin">Perp only; the liquidation price sits this much closer than 1/leverage.</param>
public sealed record HedgeSettings(
    HedgeInstrument Instrument,
    double Ratio,
    double AnnualRate,
    double IncomeApr,
    double Leverage = 3,
    double TradingFee = 0.0005,
    int HoldingDays = 30,
    double MaintenanceMargin = 0.005);

/// <param name="Token">0 or 1: the token that is shorted.</param>
/// <param name="Amount">Short size now, in tokens.</param>
/// <param name="AtLowerBound">Short size that keeps the same ratio when the pool price is at the lower bound of the range.</param>
/// <param name="AtUpperBound">Same at the upper bound.</param>
/// <param name="RehedgeIfDown">Change of the short (negative: cut it) after the volatile token has moved −5%.</param>
/// <param name="RehedgeIfUp">Same after +5%.</param>
/// <param name="LiquidationPriceUsd">Perp only: USD price of the shorted token at which the short is liquidated.</param>
public sealed record ShortLeg(
    int Token,
    double Amount,
    double NotionalUsd,
    double AtLowerBound,
    double AtUpperBound,
    double NotionalAtLowerBoundUsd,
    double NotionalAtUpperBoundUsd,
    double RehedgeIfDown,
    double RehedgeIfUp,
    double? LiquidationPriceUsd);

/// <param name="MovePct">Move of the volatile token against the stable one over the holding period.</param>
/// <param name="PoolPrice">Pool price (token1 per token0) after that move.</param>
/// <param name="DaysInRange">Days of the holding period the price spends inside the range on a straight path to the scenario price.</param>
/// <param name="PriceEffectUsd">Value change of the position from the price move alone (fees excluded).</param>
/// <param name="HedgePnlUsd">Profit (+) or loss (−) of a short sized at entry.</param>
/// <param name="HedgeCostUsd">Funding or borrow carry plus trading fees; positive is a cost.</param>
public sealed record HedgeScenario(
    double MovePct,
    double PoolPrice,
    double DaysInRange,
    double FeesUsd,
    double PriceEffectUsd,
    double HedgePnlUsd,
    double HedgeCostUsd,
    double EntryCostUsd,
    double NoHedgeNetUsd,
    double HedgedNetUsd);

/// <param name="CarryPerDayUsd">Signed: positive means the hedge earns this per day, negative that it costs.</param>
/// <param name="HedgeCostUsd">Over the holding period: −carry plus open and close trading fees. Positive is a cost.</param>
/// <param name="CapitalUsedUsd">Position value plus the perp margin.</param>
/// <param name="LiquidationMovePct">Perp only: how far the shorted token can rise before liquidation, in percent.</param>
/// <param name="LiquidationPoolPrice">Perp only, one volatile token: the pool price (token1 per token0) at which that happens.</param>
/// <param name="LiquidatesBeforeRangeTop">The short is liquidated before the volatile token reaches the top of the range (the main way a perp hedge fails).</param>
/// <param name="HedgedNetApr">Fees less hedge cost over the holding period, annualised on <see cref="CapitalUsedUsd"/>; excludes the entry cost.</param>
/// <param name="LowerBoundMovePct">Where the range edges sit as a move of the volatile token from today's price; for the chart markers.</param>
/// <param name="Scenarios">Every <c>step</c> from −30% to +30%; empty unless exactly one leg is volatile.</param>
public sealed record HedgePlan(
    HedgedLegs Legs,
    HedgeInstrument Instrument,
    double Ratio,
    IReadOnlyList<ShortLeg> Shorts,
    double NotionalUsd,
    double CarryPerDayUsd,
    double TradingFeesUsd,
    double HedgeCostUsd,
    double MarginUsd,
    double CapitalUsedUsd,
    double? LiquidationMovePct,
    double? LiquidationPoolPrice,
    bool LiquidatesBeforeRangeTop,
    double FeesUsd,
    double UnhedgedApr,
    double HedgedNetApr,
    double LowerBoundMovePct,
    double UpperBoundMovePct,
    IReadOnlyList<HedgeScenario> Scenarios,
    double NoHedgeMinUsd,
    double NoHedgeMaxUsd,
    double HedgedMinUsd,
    double HedgedMaxUsd);

/// <summary>
/// What it costs and does to hedge a freshly opened range: a short of the position's token amount (its delta) sized at
/// entry, the cost of carrying it, and the 30-day result across price moves with and without it. Pure maths.
///
/// A static hedge drifts as the price moves inside the range; the scenarios keep the entry size, and the short sizes at
/// the range edges show what would have to be adjusted. Fees stop when the price leaves the range, modelled by the
/// share of a straight price path (entry to scenario price) that lies inside it.
/// </summary>
public static class HedgePlanner
{
    public const double MinMovePct = -30;
    public const double MaxMovePct = 30;
    public const double RehedgeMove = 0.05;

    /// <summary>Which legs are volatile, by symbol (see <see cref="AssetClassifier"/>; unknown tokens count as volatile).</summary>
    public static HedgedLegs ClassifyLegs(string? symbol0, string? symbol1)
    {
        var v0 = AssetClassifier.Classify(symbol0) != AssetClass.Stablecoin;
        var v1 = AssetClassifier.Classify(symbol1) != AssetClass.Stablecoin;
        return v0 && v1 ? HedgedLegs.Both : v0 ? HedgedLegs.Token0 : v1 ? HedgedLegs.Token1 : HedgedLegs.None;
    }

    /// <summary>Share of a straight path from <paramref name="from"/> to <paramref name="to"/> that lies inside [lower, upper].</summary>
    public static double InRangeShare(double from, double to, double lower, double upper)
    {
        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        if (hi - lo < 1e-12) return from >= lower && from <= upper ? 1 : 0;
        return Math.Max(0, Math.Min(hi, upper) - Math.Max(lo, lower)) / (hi - lo);
    }

    /// <summary>Value of the liquidity at <paramref name="price"/>, in units of the quote token (price is quote per base).</summary>
    public static double ValueInQuote(double liquidity, double price, double lower, double upper)
    {
        var (x, y) = PositionMath.AmountsPerLiquidity(price, lower, upper);
        return liquidity * (x * price + y);
    }

    public static HedgePlan Plan(
        EntryPlan entry, PoolPrices prices, SnappedRange range, HedgedLegs legs, HedgeSettings settings, int scenarioStepPct = 2)
    {
        if (scenarioStepPct < 1) throw new ArgumentOutOfRangeException(nameof(scenarioStepPct));
        var h = Math.Clamp(settings.Ratio, 0, 1);
        var days = Math.Max(1, settings.HoldingDays);
        var perp = settings.Instrument == HedgeInstrument.PerpShort;
        var lev = Math.Max(1, settings.Leverage);
        var liqMove = 1 / lev - settings.MaintenanceMargin;

        double pool = prices.Pool, pa = range.PriceLower, pb = range.PriceUpper;
        var volIs1 = legs == HedgedLegs.Token1;
        var (x, y) = PositionMath.AmountsPerLiquidity(pool, pa, pb);
        var l = entry.Liquidity;

        // Price move of the volatile token as a pool-price factor: token0 volatile -> pool price follows it, token1 -> inverse.
        double PoolPriceAt(double move) => volIs1 ? pool / (1 + move) : pool * (1 + move);
        double Leg(double[] amounts, int token) => amounts[token] * h;
        double[] AmountsAt(double poolPrice)
        {
            var (ax, ay) = PositionMath.AmountsPerLiquidity(poolPrice, pa, pb);
            return [l * ax, l * ay];
        }

        // Per-leg shorts.
        var tokens = legs switch { HedgedLegs.Token0 => new[] { 0 }, HedgedLegs.Token1 => new[] { 1 }, HedgedLegs.Both => new[] { 0, 1 }, _ => [] };
        double[] now = [entry.Amount0, entry.Amount1];
        double[] usd = [prices.Usd0, prices.Usd1];
        double[] atLow = AmountsAt(pa), atHigh = AmountsAt(pb);
        double[] down = AmountsAt(PoolPriceAt(-RehedgeMove)), up = AmountsAt(PoolPriceAt(RehedgeMove));
        var shorts = new List<ShortLeg>();
        foreach (var t in tokens)
        {
            // Value of the leg at a range edge: with one volatile token the pool price turns it into USD through the stable
            // leg; with two, only the reference price is available.
            double EdgeUsd(double amount, double edgePrice) => legs switch
            {
                HedgedLegs.Token0 => amount * edgePrice * prices.Usd1,
                HedgedLegs.Token1 => amount / edgePrice * prices.Usd0,
                _ => amount * usd[t]
            };
            var amount = now[t] * h;
            shorts.Add(new ShortLeg(
                t, amount, amount * usd[t],
                Leg(atLow, t), Leg(atHigh, t),
                EdgeUsd(Leg(atLow, t), pa), EdgeUsd(Leg(atHigh, t), pb),
                Leg(down, t) - amount, Leg(up, t) - amount,
                perp && amount > 0 ? usd[t] * (1 + liqMove) : null));
        }

        var notional = shorts.Sum(s => s.NotionalUsd);
        var rate = settings.AnnualRate * (perp ? 1 : -1);          // + = the hedge earns
        var carryPerDay = notional * rate / 365;
        var tradingFees = notional * settings.TradingFee * 2;
        var hedgeCost = -carryPerDay * days + tradingFees;
        var margin = perp ? notional / lev : 0;
        var capital = entry.ValueUsd + margin;
        var feesFull = entry.ValueUsd * settings.IncomeApr * days / 365;
        var hedgedApr = capital > 0 ? (feesFull - hedgeCost) / capital * 365 / days : 0;

        // Range edges as moves of the volatile token (pool-price terms when there is no single volatile token).
        double lowMove = (volIs1 ? pool / pb : pa / pool) - 1;
        double highMove = (volIs1 ? pool / pa : pb / pool) - 1;

        double? liqPoolPrice = null;
        var liqBeforeTop = false;
        if (perp && legs is HedgedLegs.Token0 or HedgedLegs.Token1)
        {
            liqPoolPrice = PoolPriceAt(liqMove);
            // Reaching the top of the range means the volatile token rising by highMove.
            liqBeforeTop = h > 0 && liqMove < highMove;
        }

        var scenarios = new List<HedgeScenario>();
        if (legs is HedgedLegs.Token0 or HedgedLegs.Token1 && l > 0)
        {
            // Normalise to base (volatile) / quote (stable) so the maths is the same either way round.
            // The liquidity is unchanged by that: the per-liquidity amounts just swap places.
            var basePrice = volIs1 ? 1 / pool : pool;
            double lowB = volIs1 ? 1 / pb : pa, highB = volIs1 ? 1 / pa : pb;
            var quoteUsd = volIs1 ? prices.Usd0 : prices.Usd1;
            var shortBase = shorts[0].Amount;
            var v0 = ValueInQuote(l, basePrice, lowB, highB);

            HedgeScenario Evaluate(double movePct)
            {
                var p2 = basePrice * (1 + movePct / 100);
                var priceEffect = (ValueInQuote(l, p2, lowB, highB) - v0) * quoteUsd;
                var hedgePnl = shortBase * (basePrice - p2) * quoteUsd;
                var share = InRangeShare(basePrice, p2, lowB, highB);
                var fees = feesFull * share;
                var entryCost = entry.EntryCostUsd;
                return new HedgeScenario(
                    movePct, PoolPriceAt(movePct / 100), days * share, fees, priceEffect, hedgePnl, hedgeCost, entryCost,
                    priceEffect + fees - entryCost,
                    priceEffect + hedgePnl + fees - hedgeCost - entryCost);
            }

            for (var m = (int)MinMovePct; m <= (int)MaxMovePct; m += scenarioStepPct) scenarios.Add(Evaluate(m));
        }

        double Min(Func<HedgeScenario, double> f) => scenarios.Count > 0 ? scenarios.Min(f) : 0;
        double Max(Func<HedgeScenario, double> f) => scenarios.Count > 0 ? scenarios.Max(f) : 0;

        return new HedgePlan(
            legs, settings.Instrument, h, shorts, notional, carryPerDay, tradingFees, hedgeCost, margin, capital,
            perp && notional > 0 ? liqMove * 100 : null, liqPoolPrice, liqBeforeTop,
            feesFull, settings.IncomeApr, hedgedApr, lowMove * 100, highMove * 100,
            scenarios, Min(s => s.NoHedgeNetUsd), Max(s => s.NoHedgeNetUsd), Min(s => s.HedgedNetUsd), Max(s => s.HedgedNetUsd));
    }
}
