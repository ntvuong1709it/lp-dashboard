using LpLedger.Core.Models;

namespace LpLedger.Core;

public sealed record PnlEvent(EventKind Kind, double Amount0, double Amount1, decimal Usd);

public sealed record PnlInput(
    IReadOnlyList<PnlEvent> Events,
    decimal PrincipalUsd,   // value of tokens currently inside the position
    decimal Price0,         // current USD prices
    decimal Price1,
    decimal UnclaimedUsd,   // unclaimed fees or emissions
    decimal? GasUsd);

public static class PnlCalculator
{
    /// <summary>
    /// Net PnL = principal now + everything collected + unclaimed - deposited - gas.
    /// The breakdown (price effect + IL + fees - gas) sums to the same number by construction.
    /// </summary>
    public static PositionPnl Compute(PnlInput i)
    {
        decimal deposited = 0, removed = 0, collected = 0;
        double net0 = 0, net1 = 0; // tokens added minus tokens removed
        foreach (var e in i.Events)
        {
            switch (e.Kind)
            {
                case EventKind.Add:
                    deposited += e.Usd; net0 += e.Amount0; net1 += e.Amount1; break;
                case EventKind.Remove:
                    removed += e.Usd; net0 -= e.Amount0; net1 -= e.Amount1; break;
                default:
                    collected += e.Usd; break;
            }
        }
        var gas = i.GasUsd ?? 0m;
        var hodl = Usd(net0, i.Price0) + Usd(net1, i.Price1);
        var priceEffect = hodl - (deposited - removed);
        var il = i.PrincipalUsd - hodl;
        var fees = collected - removed + i.UnclaimedUsd;
        var net = i.PrincipalUsd + collected + i.UnclaimedUsd - deposited - gas;
        return new PositionPnl(
            net, deposited > 0 ? net / deposited * 100m : 0m, deposited, hodl, priceEffect, il, fees, i.GasUsd);
    }

    public static Summary Summarize(IReadOnlyList<Position> positions)
    {
        var open = positions.Where(p => p.State != PositionState.Closed).ToList();
        return new Summary(
            open.Count,
            open.Sum(p => p.ValueUsd),
            open.Sum(p => p.Rewards.UnclaimedUsd),
            positions.Sum(p => p.Pnl.DepositedUsd),
            positions.Sum(p => p.Pnl.NetUsd),
            positions.Sum(p => p.Pnl.IlUsd),
            positions.Sum(p => p.Pnl.FeesUsd));
    }

    /// <summary>Token amount times a USD price, safe against values decimal cannot hold.</summary>
    public static decimal Usd(double amount, decimal price) =>
        double.IsFinite(amount) && Math.Abs(amount) < 7e27 ? (decimal)amount * price : 0m;
}
