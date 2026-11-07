using LpDashboard.Infrastructure.Evm;

namespace LpDashboard.Domain.Tests;

public class BlockLocatorTests
{
    /// <summary>Two seconds per block up to block 100000, then five blocks per second: the shape that broke a rate estimate.</summary>
    private static long TimeOf(long block) => block <= 100_000 ? 1_000_000 + block * 2 : 1_200_000 + (block - 100_000) / 5;

    private const long Latest = 300_000;

    private static readonly BlockTimestamps Chain = (blocks, _) =>
        Task.FromResult<IReadOnlyDictionary<long, long>>(blocks.Distinct().ToDictionary(b => b, TimeOf));

    private static long BruteForce(long target, long lowest)
    {
        var found = lowest;
        for (var b = lowest; b <= Latest; b++)
        {
            if (TimeOf(b) <= target) found = b;
            else break;
        }
        return found;
    }

    [Fact]
    public async Task FindsLastBlockAtOrBeforeEachTarget_AcrossABlockTimeChange()
    {
        long[] targets = [1_000_100, 1_100_000, 1_199_999, 1_200_000, 1_200_500, 1_230_000, 1_259_999];

        var blocks = await BlockLocator.LastAtOrBeforeAsync(targets, 1, Latest, Chain, CancellationToken.None);

        for (var i = 0; i < targets.Length; i++)
        {
            Assert.Equal(BruteForce(targets[i], 1), blocks[i]);
            Assert.True(TimeOf(blocks[i]) <= targets[i]);
            Assert.True(blocks[i] == Latest || TimeOf(blocks[i] + 1) > targets[i]);
        }
        Assert.Equal(targets.Length, blocks.Distinct().Count());
    }

    [Fact]
    public async Task TargetAfterTheLatestBlock_GivesTheLatestBlock()
    {
        var blocks = await BlockLocator.LastAtOrBeforeAsync([TimeOf(Latest) + 5_000], 1, Latest, Chain, CancellationToken.None);

        Assert.Equal([Latest], blocks);
    }

    [Fact]
    public async Task TargetBeforeTheLowestBlock_GivesTheLowestBlock()
    {
        var blocks = await BlockLocator.LastAtOrBeforeAsync([TimeOf(5_000) - 1], 5_000, Latest, Chain, CancellationToken.None);

        Assert.Equal([5_000L], blocks);
    }

    [Fact]
    public async Task EachRoundIsOneBatch_SoALongHistoryStaysCheap()
    {
        var rounds = 0;
        BlockTimestamps counted = (blocks, ct) => { rounds++; return Chain(blocks, ct); };
        var targets = Enumerable.Range(0, 120).Select(d => 1_000_100L + d * 1_500).ToList();

        await BlockLocator.LastAtOrBeforeAsync(targets, 1, Latest, counted, CancellationToken.None);

        Assert.InRange(rounds, 1, 25);
    }
}
