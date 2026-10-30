using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class CollectSplitterTests
{
    [Fact]
    public void FeeOnlyCollect_IsAllFees()
    {
        var collect = Ev(PositionEventType.Collect, 2, 0, 0.1m, 20m);
        CollectSplitter.Apply([Ev(PositionEventType.IncreaseLiquidity, 1, 0, 1m, 2000m), collect]);

        Assert.Equal(0.1m, collect.Fee0);
        Assert.Equal(20m, collect.Fee1);
        Assert.Equal(0m, collect.Principal0);
    }

    [Fact]
    public void DecreaseAndCollectInSameTx_SplitsPrincipalAndFees()
    {
        var collect = Ev(PositionEventType.Collect, 5, 2, 1.1m, 2.3m);
        var owed = CollectSplitter.Apply([Ev(PositionEventType.DecreaseLiquidity, 5, 1, 1m, 2m), collect]);

        Assert.Equal(1m, collect.Principal0);
        Assert.Equal(2m, collect.Principal1);
        Assert.Equal(0.1m, collect.Fee0);
        Assert.Equal(0.3m, collect.Fee1);
        Assert.Equal((0m, 0m), owed);
    }

    [Fact]
    public void DecreaseThenCollectInLaterTx_StillConsumesPrincipalFirst()
    {
        var collect = Ev(PositionEventType.Collect, 9, 0, 1.5m, 2m);
        CollectSplitter.Apply([collect, Ev(PositionEventType.DecreaseLiquidity, 5, 0, 1m, 2m)]);

        Assert.Equal(0.5m, collect.Fee0);
        Assert.Equal(0m, collect.Fee1);
    }

    [Fact]
    public void UncollectedDecrease_IsReportedAsOwed()
    {
        var owed = CollectSplitter.Apply([Ev(PositionEventType.DecreaseLiquidity, 5, 0, 1m, 2m)]);
        Assert.Equal((1m, 2m), owed);
    }

    internal static PositionEvent Ev(PositionEventType type, long block, int logIndex, decimal a0, decimal a1) => new()
    {
        Type = type,
        BlockNumber = block,
        LogIndex = logIndex,
        TxHash = $"0x{block:x}",
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(block),
        Amount0 = a0,
        Amount1 = a1
    };
}
