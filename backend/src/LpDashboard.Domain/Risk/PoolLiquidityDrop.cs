using System.Numerics;
using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Risk;

/// <param name="DropPct">Positive when liquidity fell, negative when it grew.</param>
/// <param name="ValueThenUsd">The earlier balances valued at today's prices.</param>
public sealed record PoolLiquidityChange(decimal DropPct, decimal ValueThenUsd, decimal ValueNowUsd)
{
    public bool Exceeds(decimal thresholdPct) => DropPct > thresholdPct;
}

/// <summary>
/// Compares a pool's token balances now against an earlier snapshot, both valued at <b>current</b> prices, so a price move
/// alone (the pool rebalancing between its tokens) never reads as liquidity leaving. Pure.
/// </summary>
public static class PoolLiquidityDropDetector
{
    public static PoolLiquidityChange? Compare(
        BigInteger thenRaw0, BigInteger thenRaw1, BigInteger nowRaw0, BigInteger nowRaw1,
        int decimals0, int decimals1, decimal priceUsd0, decimal priceUsd1)
    {
        var then = Value(thenRaw0, thenRaw1, decimals0, decimals1, priceUsd0, priceUsd1);
        var now = Value(nowRaw0, nowRaw1, decimals0, decimals1, priceUsd0, priceUsd1);
        if (then <= 0) return null;
        return new PoolLiquidityChange((then - now) / then * 100m, then, now);
    }

    private static decimal Value(BigInteger raw0, BigInteger raw1, int decimals0, int decimals1, decimal price0, decimal price1) =>
        TokenAmount.FromRaw(raw0, decimals0) * price0 + TokenAmount.FromRaw(raw1, decimals1) * price1;
}
