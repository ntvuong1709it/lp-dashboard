using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Metrics;

public enum RangeTransition
{
    None,
    WentOut,
    BackIn,
}

/// <summary>Spots a position crossing its range bounds between two syncs.</summary>
public static class RangeTransitionDetector
{
    /// <param name="previousTick">Tick stored by the last sync; null when the position was never valued (first sync never alerts).</param>
    /// <param name="previousLower">Bounds at the last sync: the position may have been re-ranged since.</param>
    public static RangeTransition Detect(
        int? previousTick, int previousLower, int previousUpper, int currentTick, int currentLower, int currentUpper)
    {
        if (previousTick is not { } tick) return RangeTransition.None;

        var was = PositionMath.IsInRange(tick, previousLower, previousUpper);
        var now = PositionMath.IsInRange(currentTick, currentLower, currentUpper);
        return was == now ? RangeTransition.None : now ? RangeTransition.BackIn : RangeTransition.WentOut;
    }
}
