using LpDashboard.Domain.Metrics;

namespace LpDashboard.Application.Pools;

/// <summary>An amount of one of the pool's tokens, named by contract address (the pool's own token order is not known to the client).</summary>
public sealed record HoldingRequest(string Token, double Amount);

/// <param name="Instrument">"perp" (short perpetual) or "borrow" (borrow and sell).</param>
/// <param name="Ratio">Share of the position's delta to short, 0 to 1. 0 means no hedge.</param>
/// <param name="RatePct">Percent a year. Perp: funding (positive: the short earns it). Borrow: borrow rate (positive: you pay).</param>
/// <param name="Leverage">Perp only.</param>
/// <param name="OpenFeePct">Trading fee in percent of the notional, charged once to open and once to close.</param>
public sealed record HedgeRequest(string Instrument, double Ratio, double RatePct, double Leverage = 3, double OpenFeePct = 0.05);

/// <summary>
/// Give the range either as <c>LowerPercent</c> and <c>UpperPercent</c> (distance below and above the pool price) or as
/// <c>TickLower</c> and <c>TickUpper</c>; and the starting point either as <c>BudgetUsd</c> or as <c>Holdings</c>.
/// </summary>
/// <param name="PaysWith">With a budget: the address of the token it starts as, or "split" for half in each. Default: the stable token when the pool has one, else split.</param>
/// <param name="SwapCostPct">Swap fee plus slippage in percent of the amount sold. Default 0.10.</param>
/// <param name="GasUsd">Gas for the swap and the mint, in USD.</param>
/// <param name="IncomeAprPct">Fee (or emission) APR in percent, the basis of the hedged net APR and the scenario fees.</param>
/// <param name="IncomeBasis">"fees" or "emissions"; only labels where the income number comes from.</param>
public sealed record EntryPlanRequest(
    double? LowerPercent = null,
    double? UpperPercent = null,
    int? TickLower = null,
    int? TickUpper = null,
    double? BudgetUsd = null,
    IReadOnlyList<HoldingRequest>? Holdings = null,
    string? PaysWith = null,
    double SwapCostPct = 0.10,
    double? GasUsd = null,
    double IncomeAprPct = 0,
    string IncomeBasis = "fees",
    HedgeRequest? Hedge = null);

/// <param name="Range">The snapped range the amounts are computed from, with its actual distances from the price.</param>
/// <param name="RequestedLowerPercent">What was asked for, when the range was given in percent.</param>
/// <param name="PriceInRange">False when the pool price is outside the range: the plan is then a single token.</param>
/// <param name="Entry">Amounts, swap and costs. Amounts are in the pool's token order (<see cref="PoolStateDto.Token0"/> first).</param>
/// <param name="SwapSellToken">Address of the token to sell, null when no swap is needed.</param>
/// <param name="Hedge">Null when the request had no hedge block.</param>
public sealed record EntryPlanDto(
    PoolStateDto Pool,
    SnappedRange Range,
    double? RequestedLowerPercent,
    double? RequestedUpperPercent,
    bool PriceInRange,
    EntryPlan Entry,
    string? SwapSellToken,
    string? SwapBuyToken,
    double SwapCostPct,
    double GasUsd,
    string IncomeBasis,
    double IncomeAprPct,
    HedgePlan? Hedge);

/// <summary>
/// What to hold to enter a range of a listed pool and what hedging it would cost. Reads the pool and prices, then hands
/// everything to the pure domain planners. Nothing is stored, sent or signed.
/// </summary>
public sealed class EntryPlanService(PoolExplorerService pools)
{
    /// <summary>Gas for a swap plus a mint on an L2, when the caller gives none. A placeholder, shown and editable in the UI.</summary>
    public const double DefaultGasUsd = 0.80;

    public async Task<EntryPlanDto> PlanAsync(string poolId, EntryPlanRequest request, CancellationToken ct)
    {
        Validate(request);
        var state = await pools.ResolvePoolStateAsync(poolId, ct);
        var prices = new PoolPrices(state.PoolPrice, state.Usd0, state.Usd1);
        var legs = HedgePlanner.ClassifyLegs(state.Token0.Symbol, state.Token1.Symbol);

        SnappedRange range;
        try
        {
            range = request.TickLower is { } tl && request.TickUpper is { } tu
                ? EntryPlanner.RangeFromTicks(state.PoolPrice, tl, tu, state.TickSpacing, state.Token0.Decimals, state.Token1.Decimals)
                : EntryPlanner.SnapRange(state.PoolPrice, request.LowerPercent!.Value, request.UpperPercent!.Value, state.TickSpacing, state.Token0.Decimals, state.Token1.Decimals);
        }
        catch (ArgumentException ex)
        {
            throw Invalid(ex.Message);
        }

        var (h0, h1) = StartingHoldings(request, state, prices, legs);
        if (!(h0 > 0 || h1 > 0)) throw Invalid("Enter an amount to start from.");

        var gas = request.GasUsd ?? DefaultGasUsd;
        var entry = EntryPlanner.Plan(
            h0, h1, prices, range, request.SwapCostPct / 100, gas, (double)state.TvlUsd, state.Token0.Decimals, state.Token1.Decimals);

        HedgePlan? hedge = null;
        if (request.Hedge is { } h)
        {
            var settings = new HedgeSettings(
                string.Equals(h.Instrument, "borrow", StringComparison.OrdinalIgnoreCase) ? HedgeInstrument.BorrowAndSell : HedgeInstrument.PerpShort,
                h.Ratio, h.RatePct / 100, request.IncomeAprPct / 100, h.Leverage, h.OpenFeePct / 100);
            hedge = HedgePlanner.Plan(entry, prices, range, legs, settings);
        }

        var (sell, buy) = entry.Side switch
        {
            SwapSide.SellToken0 => (state.Token0.Address, state.Token1.Address),
            SwapSide.SellToken1 => (state.Token1.Address, state.Token0.Address),
            _ => ((string?)null, (string?)null)
        };

        return new EntryPlanDto(
            state, range, request.LowerPercent, request.UpperPercent,
            range.TickLower <= state.Tick && state.Tick < range.TickUpper,
            entry, sell, buy, request.SwapCostPct, gas,
            string.Equals(request.IncomeBasis, "emissions", StringComparison.OrdinalIgnoreCase) ? "emissions" : "fees",
            request.IncomeAprPct, hedge);
    }

    private static (double, double) StartingHoldings(EntryPlanRequest request, PoolStateDto state, PoolPrices prices, HedgedLegs legs)
    {
        if (request.Holdings is { Count: > 0 } holdings)
        {
            double h0 = 0, h1 = 0;
            foreach (var h in holdings)
            {
                if (string.Equals(h.Token, state.Token0.Address, StringComparison.OrdinalIgnoreCase)) h0 += h.Amount;
                else if (string.Equals(h.Token, state.Token1.Address, StringComparison.OrdinalIgnoreCase)) h1 += h.Amount;
                else throw Invalid($"{h.Token} is not one of this pool's tokens ({state.Token0.Symbol} {state.Token0.Address}, {state.Token1.Symbol} {state.Token1.Address}).");
            }
            return (h0, h1);
        }

        PayingWith paying;
        if (string.IsNullOrWhiteSpace(request.PaysWith))
            paying = legs switch { HedgedLegs.Token0 => PayingWith.Token1, HedgedLegs.Token1 => PayingWith.Token0, _ => PayingWith.Split };
        else if (request.PaysWith.Equals("split", StringComparison.OrdinalIgnoreCase)) paying = PayingWith.Split;
        else if (request.PaysWith.Equals(state.Token0.Address, StringComparison.OrdinalIgnoreCase)) paying = PayingWith.Token0;
        else if (request.PaysWith.Equals(state.Token1.Address, StringComparison.OrdinalIgnoreCase)) paying = PayingWith.Token1;
        else throw Invalid("paysWith must be \"split\" or the address of one of the pool's tokens.");

        return EntryPlanner.HoldingsFromBudget(request.BudgetUsd!.Value, paying, prices);
    }

    private static void Validate(EntryPlanRequest r)
    {
        var hasPercents = r.LowerPercent is not null || r.UpperPercent is not null;
        var hasTicks = r.TickLower is not null || r.TickUpper is not null;
        if (hasPercents == hasTicks) throw Invalid("Give the range either as lowerPercent and upperPercent, or as tickLower and tickUpper.");
        if (hasPercents && (r.LowerPercent is null || r.UpperPercent is null)) throw Invalid("Give both lowerPercent and upperPercent.");
        if (hasTicks && (r.TickLower is null || r.TickUpper is null)) throw Invalid("Give both tickLower and tickUpper.");
        if (hasPercents) Finite(r.LowerPercent!.Value, nameof(r.LowerPercent));
        if (hasPercents) Finite(r.UpperPercent!.Value, nameof(r.UpperPercent));

        var hasBudget = r.BudgetUsd is not null;
        var hasHoldings = r.Holdings is { Count: > 0 };
        if (hasBudget == hasHoldings) throw Invalid("Give the starting point either as budgetUsd or as holdings.");
        if (hasBudget) InRange(r.BudgetUsd!.Value, 0, 1e12, nameof(r.BudgetUsd));
        foreach (var h in r.Holdings ?? []) InRange(h.Amount, 0, 1e30, "holdings amount");

        InRange(r.SwapCostPct, 0, 10, nameof(r.SwapCostPct));
        InRange(r.GasUsd ?? 0, 0, 100_000, nameof(r.GasUsd));
        InRange(r.IncomeAprPct, 0, 10_000, nameof(r.IncomeAprPct));
        if (r.Hedge is { } h2)
        {
            InRange(h2.Ratio, 0, 1, "hedge ratio");
            InRange(h2.RatePct, -1000, 1000, "hedge ratePct");
            InRange(h2.Leverage, 1, 100, "hedge leverage");
            InRange(h2.OpenFeePct, 0, 5, "hedge openFeePct");
            if (!string.Equals(h2.Instrument, "perp", StringComparison.OrdinalIgnoreCase) && !string.Equals(h2.Instrument, "borrow", StringComparison.OrdinalIgnoreCase))
                throw Invalid("hedge instrument must be \"perp\" or \"borrow\".");
        }
    }

    private static void Finite(double v, string name)
    {
        if (!double.IsFinite(v)) throw Invalid($"{name} must be a number.");
    }

    private static void InRange(double v, double min, double max, string name)
    {
        if (!double.IsFinite(v) || v < min || v > max) throw Invalid($"{name} must be between {min:0.##} and {max:0.##}.");
    }

    private static EstimateException Invalid(string message) => new(EstimateFailure.InvalidRequest, message);
}
