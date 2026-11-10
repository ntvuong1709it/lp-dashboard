using System.Numerics;
using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pools;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Tests;

public class WalletBalanceServiceTests
{
    private const string Owner = "0x1111111111111111111111111111111111111111";
    private const string Weth = "0x4200000000000000000000000000000000000006";
    private const string Usdc = "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";

    private static WalletBalanceService Service(out FakeProvider provider, bool watched = true, bool withProvider = true)
    {
        provider = new FakeProvider();
        return new WalletBalanceService(new FakeAddresses(watched ? [Owner] : []), withProvider ? [provider] : []);
    }

    [Fact]
    public async Task ReturnsTokenAndNativeBalances_AsRawStrings_InQueryOrder()
    {
        var service = Service(out var provider);
        provider.Balances[Weth] = BigInteger.Parse("1200000000000000000");
        provider.Balances[Usdc] = 800_000_000;
        provider.Native = BigInteger.Parse("500000000000000000");

        var result = await service.GetAsync(Owner, [Usdc, Weth.ToUpperInvariant().Replace("0X", "0x")], Chain.Base, CancellationToken.None);

        Assert.Equal(Owner, result.Owner);
        Assert.Equal("500000000000000000", result.NativeRaw);
        Assert.Equal([Usdc, Weth], result.Balances.Select(b => b.Token));
        Assert.Equal(["800000000", "1200000000000000000"], result.Balances.Select(b => b.Raw));
    }

    [Fact]
    public async Task FailedReads_AreNull_NotZero()
    {
        var service = Service(out var provider);
        provider.Balances[Weth] = 5;

        var result = await service.GetAsync(Owner, [Weth, Usdc], Chain.Base, CancellationToken.None);

        Assert.Equal("5", result.Balances[0].Raw);
        Assert.Null(result.Balances[1].Raw);
        Assert.Null(result.NativeRaw);
    }

    [Fact]
    public async Task OnlyWatchedWallets_AreServed_SoItCannotBurnRpcQuota()
    {
        var service = Service(out var provider, watched: false);
        var ex = await Assert.ThrowsAsync<WalletBalanceException>(() => service.GetAsync(Owner, [Weth], Chain.Base, CancellationToken.None));
        Assert.Equal(WalletBalanceFailure.NotWatched, ex.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task BadTokens_AndTooManyTokens_AreRejectedBeforeAnyRpcCall()
    {
        var service = Service(out var provider);

        var bad = await Assert.ThrowsAsync<WalletBalanceException>(() => service.GetAsync(Owner, ["0xnope"], Chain.Base, CancellationToken.None));
        Assert.Equal(WalletBalanceFailure.InvalidToken, bad.Failure);

        var many = Enumerable.Range(0, WalletBalanceService.MaxTokens + 1).Select(i => $"0x{i:x40}").ToList();
        var tooMany = await Assert.ThrowsAsync<WalletBalanceException>(() => service.GetAsync(Owner, many, Chain.Base, CancellationToken.None));
        Assert.Equal(WalletBalanceFailure.TooManyTokens, tooMany.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task NoProviderForTheChain_IsReported()
    {
        var service = Service(out _, withProvider: false);
        var ex = await Assert.ThrowsAsync<WalletBalanceException>(() => service.GetAsync(Owner, [Weth], Chain.Base, CancellationToken.None));
        Assert.Equal(WalletBalanceFailure.NoProvider, ex.Failure);
    }

    [Fact]
    public async Task DuplicateTokens_AreReadOnce()
    {
        var service = Service(out var provider);
        provider.Balances[Weth] = 1;
        var result = await service.GetAsync(Owner, [Weth, Weth.ToUpperInvariant().Replace("0X", "0x")], Chain.Base, CancellationToken.None);
        Assert.Single(result.Balances);
        Assert.Equal(1, provider.TokenQueries);
    }

    private sealed class FakeAddresses(IReadOnlyList<string> addresses) : IWatchedAddressRepository
    {
        public Task<List<WatchedAddress>> ListAsync(CancellationToken ct) =>
            Task.FromResult(addresses.Select(a => new WatchedAddress { Address = a }).ToList());
        public Task<WatchedAddress?> GetAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<WatchedAddress?> FindAsync(string address, CancellationToken ct) =>
            Task.FromResult(addresses.Contains(address) ? new WatchedAddress { Address = address } : null);
        public void Add(WatchedAddress address) => throw new NotSupportedException();
        public void Remove(WatchedAddress address) => throw new NotSupportedException();
    }

    private sealed class FakeProvider : IPositionProvider
    {
        public Dictionary<string, BigInteger> Balances { get; } = new(StringComparer.OrdinalIgnoreCase);
        public BigInteger? Native { get; set; }
        public int Calls { get; private set; }
        public int TokenQueries { get; private set; }

        public Chain Chain => Chain.Base;
        public Protocol Protocol => Protocol.UniswapV3;
        public string NativePriceKey => "native";
        public string GetPriceKey(string tokenAddress) => tokenAddress;

        public Task<IReadOnlyList<BigInteger?>> GetTokenBalancesAsync(IReadOnlyList<(string Holder, string Token)> queries, CancellationToken ct)
        {
            Calls++;
            TokenQueries += queries.Count;
            return Task.FromResult<IReadOnlyList<BigInteger?>>(queries.Select(q => Balances.TryGetValue(q.Token, out var b) ? (BigInteger?)b : null).ToList());
        }

        public Task<IReadOnlyList<BigInteger?>> GetNativeBalancesAsync(IReadOnlyList<string> holders, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<BigInteger?>>(holders.Select(_ => Native).ToList());
        }

        public Task<long> GetLatestBlockAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null) => throw new NotSupportedException();
        public Task<PositionChainState> GetStateAsync(string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct) => throw new NotSupportedException();
    }
}
