using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class RangeTransitionTests
{
    [Fact]
    public void Leaving_the_range_is_reported() =>
        Assert.Equal(RangeTransition.WentOut, RangeTransitionDetector.Detect(100, 0, 200, 250, 0, 200));

    [Fact]
    public void Returning_to_the_range_is_reported() =>
        Assert.Equal(RangeTransition.BackIn, RangeTransitionDetector.Detect(250, 0, 200, 150, 0, 200));

    [Fact]
    public void Staying_in_or_out_is_not_reported()
    {
        Assert.Equal(RangeTransition.None, RangeTransitionDetector.Detect(100, 0, 200, 120, 0, 200));
        Assert.Equal(RangeTransition.None, RangeTransitionDetector.Detect(250, 0, 200, 300, 0, 200));
    }

    [Fact]
    public void Upper_bound_is_exclusive() =>
        Assert.Equal(RangeTransition.WentOut, RangeTransitionDetector.Detect(199, 0, 200, 200, 0, 200));

    [Fact]
    public void Never_valued_position_does_not_alert() =>
        Assert.Equal(RangeTransition.None, RangeTransitionDetector.Detect(null, 0, 200, 500, 0, 200));

    [Fact]
    public void Compares_against_the_previous_bounds()
    {
        // Same tick, but the range moved away from it between syncs.
        Assert.Equal(RangeTransition.WentOut, RangeTransitionDetector.Detect(100, 0, 200, 100, 300, 400));
    }
}
