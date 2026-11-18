namespace LpDashboard.Domain.Market;

public enum FearGreedZone { ExtremeFear, Fear, Neutral, Greed, ExtremeGreed }

/// <summary>The values of one zone, both ends included.</summary>
public sealed record FearGreedBand(FearGreedZone Zone, int From, int To);

/// <summary>
/// The five zones of the crypto Fear and Greed index (0 to 100), as the index labels them. Colours and words both come
/// from here, never from the label text of the source, so they cannot disagree.
/// </summary>
public static class FearGreedZones
{
    public static IReadOnlyList<FearGreedBand> Bands { get; } =
    [
        new(FearGreedZone.ExtremeFear, 0, 25),
        new(FearGreedZone.Fear, 26, 46),
        new(FearGreedZone.Neutral, 47, 54),
        new(FearGreedZone.Greed, 55, 75),
        new(FearGreedZone.ExtremeGreed, 76, 100),
    ];

    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 100.</exception>
    public static FearGreedZone Of(int value)
    {
        foreach (var band in Bands)
            if (value >= band.From && value <= band.To) return band.Zone;
        throw new ArgumentOutOfRangeException(nameof(value), value, "The index runs from 0 to 100.");
    }
}
