using System.Numerics;
using LpDashboard.Application;
using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pools;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using LpDashboard.Infrastructure;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LpDashboard.Domain.Tests;

/// <summary>
/// The real risk services, repositories and a throwaway SQLite file, with fake sources, prices, chain and Telegram: nothing
/// touches the network. Each <see cref="Service{T}"/> call uses a fresh scope, like a request.
/// </summary>
internal sealed class RiskHarness : IDisposable
{
    public static readonly DateTime T0 = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lpdash-risk-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public FakeGoPlus GoPlus { get; } = new();
    public FakeHoneypot Honeypot { get; } = new();
    public FakeOnChain OnChain { get; } = new();
    public FakeMarket Market { get; } = new();
    public Clock Time { get; } = new(T0);
    public RecordingNotifier Notifier { get; } = new();
    public FakePrices Prices { get; } = new();
    public FakeChain Provider { get; } = new();

    public RiskHarness(Action<RiskSettings>? configure = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(config);
        services.AddApplication();
        services.Configure<RiskSettings>(s =>
        {
            s.SourceTimeoutSeconds = 1;
            s.HistoryKeep = 3;
            s.NightlyPauseSeconds = 0;
            configure?.Invoke(s);
        });
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IGoPlusSource>(GoPlus);
        services.AddSingleton<IHoneypotSource>(Honeypot);
        services.AddSingleton<IOnChainFactsSource>(OnChain);
        services.AddSingleton<IMarketFactsSource>(Market);
        services.AddSingleton<IContractInfoSource>(new NoContractInfo());
        services.AddSingleton<IPriceSource>(Prices);
        services.AddSingleton<IPoolStatsSource>(new EmptyPoolStats());
        services.AddSingleton<IPositionProvider>(Provider);
        services.AddSingleton<IAlertNotifier>(Notifier);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public T Service<T>() where T : notnull => _services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    public static GoPlusFacts GoPlusOk() => new(false, 0, 0, false, false, false, null, null, 0, true, false, 100, []);

    /// <summary>Adds a watched wallet with one open position in a pool of <paramref name="token0"/> and <paramref name="token1"/>.</summary>
    public async Task AddHeldPositionAsync(string owner, string pool, string token0, string token1, int decimals0 = 18, int decimals1 = 6)
    {
        await using var db = NewDb();
        if (!await db.WatchedAddresses.AnyAsync(a => a.Address == owner))
            db.WatchedAddresses.Add(new WatchedAddress { Address = owner, ChainFamily = ChainFamily.Evm, CreatedAt = T0 });
        var t0 = await db.Tokens.FirstOrDefaultAsync(t => t.Address == token0) ?? new Token { Chain = Domain.Chain.Base, Address = token0, Symbol = "AAA", Decimals = decimals0 };
        var t1 = await db.Tokens.FirstOrDefaultAsync(t => t.Address == token1) ?? new Token { Chain = Domain.Chain.Base, Address = token1, Symbol = "BBB", Decimals = decimals1 };
        db.Positions.Add(new Position
        {
            Chain = Domain.Chain.Base, Protocol = Protocol.UniswapV3, PositionManager = "0xmanager", TokenId = Guid.NewGuid().ToString("N")[..8], Owner = owner,
            Status = PositionStatus.Open,
            Pool = new Pool { Chain = Domain.Chain.Base, Protocol = Protocol.UniswapV3, Address = pool, Token0 = t0, Token1 = t1, Fee = 500, TickSpacing = 10 },
        });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    // ---- fakes

    public sealed class Clock(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    public sealed class RecordingNotifier : IAlertNotifier
    {
        public List<string> Messages { get; } = [];
        public bool Configured { get; set; } = true;
        public string? FailWith { get; set; }
        public int Attempts { get; private set; }
        public bool IsConfigured => Configured;

        public Task<string?> SendAsync(string message, CancellationToken ct)
        {
            Attempts++;
            if (FailWith is null) Messages.Add(message);
            return Task.FromResult(FailWith);
        }
    }

    public sealed class FakeGoPlus : IGoPlusSource
    {
        public int Calls { get; private set; }
        public List<string> Tokens { get; } = [];
        public bool Throw { get; set; }

        /// <summary>Per-token override; otherwise <see cref="Reply"/>, otherwise a clean answer.</summary>
        public Dictionary<string, GoPlusFacts> ByToken { get; } = new(StringComparer.OrdinalIgnoreCase);
        public GoPlusFacts? Reply { get; set; }

        public Task<SourceOutcome<GoPlusFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
        {
            Calls++;
            Tokens.Add(token);
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(SourceOutcome<GoPlusFacts>.Ok(ByToken.GetValueOrDefault(token) ?? Reply ?? GoPlusOk()));
        }
    }

    public sealed class FakeHoneypot : IHoneypotSource
    {
        public int Calls { get; private set; }
        public List<string> Tokens { get; } = [];
        public bool Hang { get; set; }

        public async Task<SourceOutcome<HoneypotIsFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
        {
            Calls++;
            Tokens.Add(token);
            if (Hang) await Task.Delay(Timeout.Infinite, ct);
            return SourceOutcome<HoneypotIsFacts>.Ok(new HoneypotIsFacts(false, false, 0, 0, null));
        }
    }

    public sealed class FakeOnChain : IOnChainFactsSource
    {
        public int TokenCalls { get; private set; }
        public List<string> Tokens { get; } = [];
        public bool NoContract { get; set; }
        public Dictionary<string, OnChainFacts> ByToken { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Supports(Chain chain) => true;

        public Task<SourceOutcome<OnChainReading>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
        {
            TokenCalls++;
            Tokens.Add(token);
            if (NoContract) return Task.FromResult(SourceOutcome<OnChainReading>.Fail(OnChainReading.NoContract));
            var facts = ByToken.GetValueOrDefault(token) ?? new OnChainFacts(RiskAddresses.Zero, null, null, null, null);
            return Task.FromResult(SourceOutcome<OnChainReading>.Ok(new OnChainReading(facts, "TKN")));
        }

        public Task<SourceOutcome<GaugeStatus>> GetGaugeStatusAsync(Chain chain, string pool, CancellationToken ct) =>
            Task.FromResult(SourceOutcome<GaugeStatus>.Ok(new GaugeStatus(false, null)));
    }

    public sealed class FakeMarket : IMarketFactsSource
    {
        public int TokenCalls { get; private set; }
        public List<string> Tokens { get; } = [];

        public Task<SourceOutcome<MarketFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
        {
            TokenCalls++;
            Tokens.Add(token);
            return Task.FromResult(SourceOutcome<MarketFacts>.Ok(new MarketFacts(5_000_000, 100_000, 3000, T0.AddDays(-300), [])));
        }

        public Task<SourceOutcome<PoolMarketFacts>> GetPoolAsync(Chain chain, string pool, CancellationToken ct) =>
            Task.FromResult(SourceOutcome<PoolMarketFacts>.Ok(new PoolMarketFacts(T0.AddDays(-100), 1_000_000)));
    }

    public sealed class NoContractInfo : IContractInfoSource
    {
        public bool IsConfigured => false;

        public Task<SourceOutcome<ContractFacts>> GetTokenAsync(Chain chain, string token, CancellationToken ct) =>
            throw new InvalidOperationException("Etherscan has no key and must not be asked.");
    }

    public sealed class FakePrices : IPriceSource
    {
        /// <summary>USD price by price key (lower-cased token address); anything else is $2.</summary>
        public Dictionary<string, decimal> ByKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(keys.ToDictionary(k => k, k => ByKey.GetValueOrDefault(k, 2m), StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, decimal>> GetHistoricalAsync(IReadOnlyCollection<string> keys, long timestamp, CancellationToken ct) =>
            GetCurrentAsync(keys, ct);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>> GetChartAsync(
            IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<PricePoint>>>(new Dictionary<string, IReadOnlyList<PricePoint>>());
    }

    public sealed class EmptyPoolStats : IPoolStatsSource
    {
        public Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PoolStats>>([]);

        public Task<IReadOnlyList<PoolChartPoint>> GetChartAsync(string poolId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PoolChartPoint>>([]);
    }

    /// <summary>A chain provider that only knows balances: what the liquidity snapshots read.</summary>
    public sealed class FakeChain : IPositionProvider
    {
        /// <summary>Raw balance by (holder, token), lower-cased.</summary>
        public Dictionary<(string Holder, string Token), BigInteger> Balances { get; } = [];

        public Chain Chain => Domain.Chain.Base;
        public Protocol Protocol => Protocol.UniswapV3;
        public string NativePriceKey => "native";
        public string GetPriceKey(string tokenAddress) => tokenAddress.ToLowerInvariant();

        public void Set(string pool, string token, BigInteger raw) => Balances[(pool.ToLowerInvariant(), token.ToLowerInvariant())] = raw;

        public Task<IReadOnlyList<BigInteger?>> GetTokenBalancesAsync(IReadOnlyList<(string Holder, string Token)> queries, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BigInteger?>>(queries
                .Select(q => Balances.TryGetValue((q.Holder.ToLowerInvariant(), q.Token.ToLowerInvariant()), out var b) ? (BigInteger?)b : null)
                .ToList());

        public Task<long> GetLatestBlockAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(
            string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null) =>
            throw new NotSupportedException();

        public Task<PositionChainState> GetStateAsync(string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
