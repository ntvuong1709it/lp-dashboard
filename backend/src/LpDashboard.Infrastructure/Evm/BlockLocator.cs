namespace LpDashboard.Infrastructure.Evm;

/// <summary>Block timestamps (unix seconds) by block number.</summary>
internal delegate Task<IReadOnlyDictionary<long, long>> BlockTimestamps(IReadOnlyCollection<long> blocks, CancellationToken ct);

/// <summary>
/// Finds blocks by time. Block time is not constant (a chain can change it, and Base has), so a rate estimate is wrong for
/// any date far from the one it was measured at; searching the timestamps themselves does not depend on the rate.
/// </summary>
internal static class BlockLocator
{
    /// <summary>
    /// For each target time, the last block in [<paramref name="lowest"/>, <paramref name="latest"/>] whose timestamp is at or
    /// before it. A target before the lowest block gives the lowest block; one after the latest gives the latest. All targets are
    /// searched together, so a search costs about log2(range) rounds of one batched timestamp lookup each.
    /// </summary>
    public static async Task<IReadOnlyList<long>> LastAtOrBeforeAsync(
        IReadOnlyList<long> targets, long lowest, long latest, BlockTimestamps timestamps, CancellationToken ct,
        Action<int, int>? round = null)
    {
        if (targets.Count == 0) return [];
        lowest = Math.Clamp(lowest, 1, latest);

        var known = new Dictionary<long, long>(await timestamps([lowest, latest], ct));
        var low = new long[targets.Count];
        var high = new long[targets.Count];
        var open = new List<int>();
        for (var i = 0; i < targets.Count; i++)
        {
            // Invariant while open: timestamp(low) <= target < timestamp(high).
            if (known[latest] <= targets[i]) low[i] = high[i] = latest;
            else if (known[lowest] > targets[i]) low[i] = high[i] = lowest;
            else
            {
                low[i] = lowest;
                high[i] = latest;
                open.Add(i);
            }
        }

        // Each round halves the range, so this many rounds are needed at most (reported as (done, total) for progress).
        var totalRounds = (int)Math.Ceiling(Math.Log2(Math.Max(2, latest - lowest)));
        var done = 0;
        while (true)
        {
            open.RemoveAll(i => high[i] - low[i] <= 1);
            if (open.Count == 0) break;
            round?.Invoke(Math.Min(done++, totalRounds), totalRounds);

            var mids = open.Select(i => low[i] + (high[i] - low[i]) / 2).Where(m => !known.ContainsKey(m)).Distinct().ToList();
            if (mids.Count > 0)
            {
                foreach (var (block, time) in await timestamps(mids, ct)) known[block] = time;
            }

            foreach (var i in open)
            {
                var mid = low[i] + (high[i] - low[i]) / 2;
                if (known[mid] <= targets[i]) low[i] = mid;
                else high[i] = mid;
            }
        }

        return low;
    }
}
