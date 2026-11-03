using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class PnlHistoryTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Open_position_tracks_value_plus_flows_and_ends_at_final_pnl()
    {
        var series = new PnlSeries(
            [new(new DateTime(2026, 1, 10, 1, 0, 0, DateTimeKind.Utc), 1000, 0),
             new(new DateTime(2026, 2, 10, 1, 0, 0, DateTimeKind.Utc), 1100, 0)],
            [new(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), -1000)],
            Now, 150);

        var h = PnlHistoryCalculator.Build([series], Now);

        Assert.Equal(0, h.Points[0].CumulativePnlUsd);
        Assert.Equal(150, h.Points[^1].CumulativePnlUsd);
        Assert.Equal(["2026-01", "2026-02", "2026-03"], h.Months.Select(m => m.Month));
        Assert.Equal(150, h.Months.Sum(m => m.NetPnlUsd));
    }

    [Fact]
    public void Closed_position_settles_at_final_pnl_after_close()
    {
        var closed = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var series = new PnlSeries(
            [new(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), 1000, 0)],
            [new(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), -1000)],
            closed, 80);

        var h = PnlHistoryCalculator.Build([series], Now);

        Assert.Equal(80, h.Points[^1].CumulativePnlUsd);
    }

    [Fact]
    public void No_positions_gives_empty_history() =>
        Assert.Empty(PnlHistoryCalculator.Build([], Now).Points);
}
