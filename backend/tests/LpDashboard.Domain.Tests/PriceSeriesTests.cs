using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LpDashboard.Domain.Tests;

public class PriceSeriesTests
{
    private const long Hour = 3600;
    private static readonly long Start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Fact]
    public async Task LongSeries_IsFetchedInWindowsTheChartApiAccepts_AndFullyResolved()
    {
        var source = new RecordingSource();
        var service = new PriceService(source, new MemoryPriceRepository(), TimeProvider.System, NullLogger<PriceService>.Instance);

        var prices = await service.GetSeriesAsync(["base:0xa", "base:0xb"], Start, Hour, 1332, CancellationToken.None);

        Assert.True(source.Calls.Count >= 6, "1332 hourly points of 2 tokens need several windows");
        Assert.Equal(1332 * 2, prices.Count);
        Assert.All(prices.Values, p => Assert.NotNull(p));
    }

    [Fact]
    public async Task SecondRequest_IsServedFromTheCache()
    {
        var source = new RecordingSource();
        var repository = new MemoryPriceRepository();
        var first = new PriceService(source, repository, TimeProvider.System, NullLogger<PriceService>.Instance);
        await first.GetSeriesAsync(["base:0xa"], Start, Hour, 300, CancellationToken.None);
        var callsAfterFirst = source.Calls.Count;

        var second = new PriceService(source, repository, TimeProvider.System, NullLogger<PriceService>.Instance);
        await second.GetSeriesAsync(["base:0xa"], Start, Hour, 300, CancellationToken.None);

        Assert.Equal(callsAfterFirst, source.Calls.Count);
    }

    private sealed class RecordingSource : IPriceSource
    {
        public List<(long Start, int Count)> Calls { get; } = [];

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
            IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct)
        {
            // The live API answers 400 above 500 points per request (tokens x span).
            if (keys.Count * count > 500) throw new HttpRequestException("400 (Bad Request)");

            Calls.Add((start, count));
            IReadOnlyList<PricePoint> points = Enumerable.Range(0, count).Select(i => new PricePoint(start + i * stepSeconds, 100m)).ToList();
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>>(keys.ToDictionary(k => k, _ => points));
        }

        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
    }

    private sealed class MemoryPriceRepository : ITokenPriceRepository
    {
        private readonly List<TokenPrice> _rows = [];

        public Task<List<TokenPrice>> FindAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<long> timestamps, CancellationToken ct) =>
            Task.FromResult(_rows.Where(r => keys.Contains(r.PriceKey) && timestamps.Contains(r.Timestamp)).ToList());

        public void Add(TokenPrice price) => _rows.Add(price);
    }
}
