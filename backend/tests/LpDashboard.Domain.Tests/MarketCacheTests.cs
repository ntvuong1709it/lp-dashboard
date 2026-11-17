using LpDashboard.Application.Market;

namespace LpDashboard.Domain.Tests;

/// <summary>A clock the tests move by hand.</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class MarketCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StaleMax = TimeSpan.FromHours(24);
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Second_call_within_the_ttl_does_not_fetch()
    {
        var cache = new MarketCache(new FakeClock(Start));
        var calls = 0;
        Task<string?> Fetch(CancellationToken _) { calls++; return Task.FromResult<string?>("a"); }

        await cache.GetOrFetchAsync("k", Ttl, StaleMax, Fetch, default);
        var (value, state) = await cache.GetOrFetchAsync("k", Ttl, StaleMax, Fetch, default);

        Assert.Equal(1, calls);
        Assert.Equal("a", value);
        Assert.Equal(CacheState.Fresh, state);
    }

    [Fact]
    public async Task After_the_ttl_it_fetches_again()
    {
        var clock = new FakeClock(Start);
        var cache = new MarketCache(clock);
        var n = 0;
        Task<string?> Fetch(CancellationToken _) => Task.FromResult<string?>($"v{++n}");

        await cache.GetOrFetchAsync("k", Ttl, StaleMax, Fetch, default);
        clock.Advance(Ttl + TimeSpan.FromSeconds(1));
        var (value, _) = await cache.GetOrFetchAsync("k", Ttl, StaleMax, Fetch, default);

        Assert.Equal("v2", value);
    }

    [Fact]
    public async Task A_failed_refresh_serves_the_old_value_as_stale_until_the_limit()
    {
        var clock = new FakeClock(Start);
        var cache = new MarketCache(clock);
        await cache.GetOrFetchAsync("k", Ttl, StaleMax, _ => Task.FromResult<string?>("old"), default);

        clock.Advance(TimeSpan.FromHours(2));
        var stale = await cache.GetOrFetchAsync("k", Ttl, StaleMax, _ => Task.FromResult<string?>(null), default);
        Assert.Equal(("old", CacheState.Stale), stale);

        clock.Advance(TimeSpan.FromHours(23));
        var gone = await cache.GetOrFetchAsync("k", Ttl, StaleMax, _ => Task.FromResult<string?>(null), default);
        Assert.Equal(((string?)null, CacheState.None), gone);
    }

    [Fact]
    public async Task A_fetch_that_throws_counts_as_a_failure()
    {
        var cache = new MarketCache(new FakeClock(Start));

        var result = await cache.GetOrFetchAsync<string>("k", Ttl, StaleMax, _ => throw new InvalidOperationException("boom"), default);

        Assert.Equal(((string?)null, CacheState.None), result);
    }

    [Fact]
    public async Task The_ttl_can_depend_on_the_value()
    {
        var clock = new FakeClock(Start);
        var cache = new MarketCache(clock);
        var calls = 0;
        Task<string?> Fetch(CancellationToken _) { calls++; return Task.FromResult<string?>("final"); }
        TimeSpan TtlFor(string v) => v == "final" ? TimeSpan.FromHours(24) : Ttl;

        await cache.GetOrFetchAsync<string>("k", TtlFor, StaleMax, Fetch, default);
        clock.Advance(TimeSpan.FromHours(5));
        await cache.GetOrFetchAsync<string>("k", TtlFor, StaleMax, Fetch, default);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_calls_on_one_key_fetch_once()
    {
        var cache = new MarketCache(new FakeClock(Start));
        var calls = 0;
        async Task<string?> Fetch(CancellationToken _) { Interlocked.Increment(ref calls); await Task.Delay(50); return "a"; }

        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => cache.GetOrFetchAsync("k", Ttl, StaleMax, Fetch, default)));

        Assert.Equal(1, calls);
        Assert.All(all, r => Assert.Equal("a", r.Value));
    }
}
