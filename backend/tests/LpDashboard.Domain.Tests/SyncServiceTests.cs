using System.Numerics;
using LpDashboard.Application;
using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Sync;
using LpDashboard.Domain;
using LpDashboard.Infrastructure;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LpDashboard.Domain.Tests;

/// <summary>Drives the real SyncService, repositories and a throwaway SQLite file with a fake chain provider and price source.</summary>
public sealed class SyncServiceTests : IDisposable
{
    private const string Owner = "0xabc0000000000000000000000000000000000001";
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lpdash-test-{Guid.NewGuid():N}.db");
    private readonly FakeProvider _provider = new();
    private readonly FakeClock _clock = new(T0.AddDays(1));
    private readonly ServiceProvider _services;

    public SyncServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(config);
        services.AddApplication();
        services.Configure<SyncOptions>(o => o.SnapshotIntervalMinutes = 60);
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IPositionProvider>(_provider);
        services.AddSingleton<IPriceSource, FakePriceSource>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task FirstSync_StoresPositionEventsSnapshotAndCursor()
    {
        var errors = await SyncAsync();

        Assert.Empty(errors);
        await using var db = NewDb();
        var position = await db.Positions.Include(p => p.Events).SingleAsync();
        Assert.Equal(PositionStatus.Open, position.Status);
        Assert.Equal("1", position.TokenId);
        Assert.Single(position.Events);
        Assert.Equal(2m, position.Events[0].Price0Usd);
        Assert.Equal(1, await db.PositionSnapshots.CountAsync());
        Assert.Equal(1000, (await db.SyncCursors.SingleAsync()).LastSyncedBlock);
    }

    [Fact]
    public async Task RepeatedSync_DoesNotDuplicateEventsOrSnapshots()
    {
        await SyncAsync();
        await SyncAsync();

        await using var db = NewDb();
        Assert.Equal(1, await db.Positions.CountAsync());
        Assert.Equal(1, await db.PositionEvents.CountAsync());
        Assert.Equal(1, await db.PositionSnapshots.CountAsync());
    }

    [Fact]
    public async Task SnapshotIsAddedOnceIntervalHasElapsed()
    {
        await SyncAsync();
        _clock.Advance(TimeSpan.FromMinutes(61));
        await SyncAsync();

        await using var db = NewDb();
        Assert.Equal(2, await db.PositionSnapshots.CountAsync());
    }

    [Fact]
    public async Task NewEventOnLaterSync_IsAppended()
    {
        await SyncAsync();
        _provider.Events.Add(Event("0xdef", 1, 1100, PositionEventType.Collect, 0.5m, 0m));
        _provider.LatestBlock = 1200;
        await SyncAsync();

        await using var db = NewDb();
        var types = await db.PositionEvents.Select(e => e.Type).ToListAsync();
        Assert.Equal(2, types.Count);
        Assert.Contains(PositionEventType.Collect, types);
        Assert.Equal(1200, (await db.SyncCursors.SingleAsync()).LastSyncedBlock);
    }

    [Fact]
    public async Task FailingPosition_ReportsErrorAndDoesNotAdvanceCursor()
    {
        await SyncAsync();
        _provider.LatestBlock = 2000;
        _provider.ThrowOnState = true;

        var errors = await SyncAsync();

        Assert.Single(errors);
        Assert.Contains("#1", errors[0]);
        await using var db = NewDb();
        Assert.Equal(1000, (await db.SyncCursors.SingleAsync()).LastSyncedBlock);

        _provider.ThrowOnState = false;
        Assert.Empty(await SyncAsync());
        await using var after = NewDb();
        Assert.Equal(2000, (await after.SyncCursors.SingleAsync()).LastSyncedBlock);
    }

    [Fact]
    public async Task BurnedPosition_IsMarkedClosed()
    {
        _provider.Burned = true;

        await SyncAsync();

        await using var db = NewDb();
        var position = await db.Positions.SingleAsync();
        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(0, await db.PositionSnapshots.CountAsync());
    }

    private async Task<IReadOnlyList<string>> SyncAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SyncService>()
            .SyncAsync(Chain.Base, Protocol.UniswapV3, Owner, CancellationToken.None);
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static RawPositionEvent Event(string tx, int logIndex, long block, PositionEventType type, decimal amount0, decimal amount1) => new(
        "0xmanager", "1", type, tx, logIndex, block, T0.AddHours(block / 100.0),
        new BigInteger(amount0 * 1_000_000_000_000_000_000m), new BigInteger(amount1 * 1_000_000_000_000_000_000m),
        null, BigInteger.Zero, Owner, 0m);

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class FakeClock(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakePriceSource : IPriceSource
    {
        // WETH-like token0 = $2, token1 = $1.
        private static decimal PriceOf(string key) => key.EndsWith("token0", StringComparison.OrdinalIgnoreCase) ? 2m : 1m;

        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.ToDictionary(k => k, PriceOf, StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            GetCurrentAsync(keys, ct);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
            IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>>(new Dictionary<string, IReadOnlyList<PricePoint>>());
    }

    private sealed class FakeProvider : IPositionProvider
    {
        private static readonly BigInteger Q96 = BigInteger.Pow(2, 96);

        public Chain Chain => Chain.Base;
        public Protocol Protocol => Protocol.UniswapV3;
        public string NativePriceKey => "native";
        public string GetPriceKey(string tokenAddress) => tokenAddress;

        public long LatestBlock { get; set; } = 1000;
        public bool ThrowOnState { get; set; }
        public bool Burned { get; set; }
        public List<RawPositionEvent> Events { get; } = [Event("0xabc", 0, 900, PositionEventType.IncreaseLiquidity, 1m, 2m)];

        public Task<long> GetLatestBlockAsync(CancellationToken ct) => Task.FromResult(LatestBlock);

        public Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DiscoveredPosition>>([new DiscoveredPosition("0xmanager", "1", 900, false, null, Burned)]);

        // Deliberately ignores the block range: SyncService must de-duplicate by (tx, log index).
        public Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(
            string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null) =>
            Task.FromResult<IReadOnlyList<RawPositionEvent>>(Events.ToList());

        public Task<PositionChainState> GetStateAsync(string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct)
        {
            if (ThrowOnState) throw new InvalidOperationException("rpc down");

            var oneEther = BigInteger.Pow(10, 18);
            return Task.FromResult(new PositionChainState(
                LatestBlock, "0xpool",
                new TokenInfo("token0", "T0", 18), new TokenInfo("token1", "T1", 18),
                3000, 60, -1000, 1000,
                Burned ? BigInteger.Zero : oneEther * 10, Q96, 0,
                BigInteger.Zero, BigInteger.Zero, null, BigInteger.Zero, Burned));
        }
    }
}
