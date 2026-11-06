using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LpDashboard.Domain.Tests;

public class MarketTickerTests
{
    [Fact]
    public void Assets_AreTheFourHeadlineSymbols_InDisplayOrder()
    {
        Assert.Equal(["BTC", "ETH", "BNB", "AERO"], MarketTickerService.Assets.Select(a => a.Symbol));
    }

    [Theory]
    [InlineData(110, 100, 10)]
    [InlineData(90, 100, -10)]
    [InlineData(100, 100, 0)]
    public void Change_IsPercentageAgainstThePreviousPrice(double current, double previous, double expected)
    {
        Assert.Equal((decimal)expected, MarketTickerService.Change((decimal)current, (decimal)previous));
    }

    [Fact]
    public void Change_IsUnknown_WithoutAPreviousPrice()
    {
        Assert.Null(MarketTickerService.Change(100m, 0m));
    }

    [Fact]
    public async Task Ticker_ReturnsEachAssetWithItsSymbolPriceAndChange()
    {
        var source = new FakeSource();
        var service = new MarketTickerService(
            new PriceService(source, new EmptyPriceRepository(), TimeProvider.System, NullLogger<PriceService>.Instance),
            source, TimeProvider.System, NullLogger<MarketTickerService>.Instance);

        var ticker = await service.GetAsync(CancellationToken.None);

        Assert.Equal(["BTC", "ETH", "BNB", "AERO"], ticker.Prices.Select(p => p.Symbol));
        Assert.All(ticker.Prices, p => Assert.Equal(200m, p.PriceUsd));
        Assert.All(ticker.Prices, p => Assert.Equal(100m, p.Change24hPercent));
    }

    private sealed class FakeSource : IPriceSource
    {
        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.ToDictionary(k => k, _ => 200m));

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.ToDictionary(k => k, _ => 100m));

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
            IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>>(new Dictionary<string, IReadOnlyList<PricePoint>>());
    }

    private sealed class EmptyPriceRepository : ITokenPriceRepository
    {
        public Task<List<TokenPrice>> FindAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<long> timestamps, CancellationToken ct) =>
            Task.FromResult(new List<TokenPrice>());

        public void Add(TokenPrice price) { }
    }
}
