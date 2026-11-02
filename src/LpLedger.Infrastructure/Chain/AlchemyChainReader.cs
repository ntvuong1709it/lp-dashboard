using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using LpLedger.Core;
using LpLedger.Core.Models;

namespace LpLedger.Infrastructure.Chain;

/// <summary>
/// Reads Aerodrome Slipstream positions on Base through an Alchemy free-tier endpoint, which caps eth_getLogs at 10 blocks.
/// Instead of scanning logs it finds positions and transaction hashes from Alchemy's transfer index, then decodes the
/// position-manager events out of those transactions' receipts. That is best-effort: a liquidity change whose transaction
/// moves none of the pair's tokens to or from the wallet and is not an NFT transfer is not found.
/// What it finds is kept in the <see cref="IScanStore"/>, so a refresh only asks about blocks after the last scan.
/// </summary>
public sealed class AlchemyChainReader(AlchemyClient rpc, IReadOnlyList<string> managers, IScanStore store) : IChainReader
{
    /// <summary>Slipstream position managers seen on Base. Verified: 0x4857 is the current one, the others are older.</summary>
    public static readonly string[] DefaultManagers =
    [
        "0x4857db576ee0e45f10525ec59527bfd10beec60b",
        "0xe1f8cd9ac4e4a65f54f38a5cdafca44f6dd68b53",
        "0x827922686190790b37229fd06084350e74485b72",
    ];

    private const string Zero = "0x0000000000000000000000000000000000000000";
    private static readonly BigInteger MaxU128 = (BigInteger.One << 128) - 1;

    /// <summary>
    /// Blocks before the head that a scan is not trusted through, so a short reorg cannot hide a transfer.
    /// The next scan starts there; rows found twice are ignored by the store.
    /// </summary>
    private const int ReorgBuffer = 30;

    private static readonly Dictionary<string, EventKind> EventTopics = new()
    {
        [Abi.Topic("IncreaseLiquidity(uint256,uint128,uint256,uint256)")] = EventKind.Add,
        [Abi.Topic("IncreaseLiquidity(uint256,uint128,uint256,uint256,int24,int24)")] = EventKind.Add,
        [Abi.Topic("DecreaseLiquidity(uint256,uint128,uint256,uint256)")] = EventKind.Remove,
        [Abi.Topic("Collect(uint256,address,uint256,uint256)")] = EventKind.Collect,
    };

    // Two lookups of one wallet must not scan the same blocks at once.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WalletLocks = new();

    private readonly HashSet<string> _managerSet = managers.Select(m => m.ToLowerInvariant()).ToHashSet();
    private readonly Lazy<Task<long>> _head = new(rpc.BlockNumberAsync);
    private readonly ConcurrentDictionary<string, List<AssetTransfer>> _nftTransfers = new();
    private readonly ConcurrentDictionary<long, long> _timestamps = new();
    private readonly ConcurrentDictionary<string, Task<RawPosition?>> _positions = new();
    private readonly ConcurrentDictionary<string, Task<string>> _factories = new();
    private readonly ConcurrentDictionary<string, TokenMeta> _tokens = new();

    private static async Task<IDisposable> LockAsync(string wallet)
    {
        var gate = WalletLocks.GetOrAdd(wallet, _ => new SemaphoreSlim(1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }

    /// <summary>
    /// Fetches transfers newer than each item's cursor, hands them to <paramref name="save"/>, and only then moves the
    /// cursors forward, so a failure part-way is simply scanned again. Items that share a cursor share one query.
    /// </summary>
    private async Task ScanAsync(
        string wallet, IReadOnlyList<string> items, Func<string, string> cursorKey,
        Func<IReadOnlyList<string>, long, Task<List<AssetTransfer>>> query, Func<List<AssetTransfer>, Task> save)
    {
        var head = await _head.Value;
        var starts = await Task.WhenAll(items.Select(async i => (Item: i, From: (await store.CursorAsync(wallet, cursorKey(i)) ?? -1) + 1)));
        foreach (var group in starts.GroupBy(s => s.From))
        {
            var rows = await query(group.Select(s => s.Item).ToList(), group.Key);
            var times = rows.Where(r => r.Timestamp > 0).GroupBy(r => r.Block).Select(g => KeyValuePair.Create(g.Key, g.First().Timestamp)).ToList();
            foreach (var (block, ts) in times) _timestamps[block] = ts;
            await store.AddTimestampsAsync(times);
            await save(rows);
            foreach (var s in group) await store.SetCursorAsync(wallet, cursorKey(s.Item), Math.Max(0, head - ReorgBuffer));
        }
    }

    private Task ScanAsync(string wallet, string key, Func<long, Task<List<AssetTransfer>>> query, Func<List<AssetTransfer>, Task> save) =>
        ScanAsync(wallet, [key], k => k, (_, from) => query(from), save);

    public async Task<IReadOnlyList<PositionRef>> CandidatesAsync(string owner)
    {
        var o = owner.ToLowerInvariant();
        using var _ = await LockAsync(o);

        Task Save(List<AssetTransfer> rows) => store.AddNftTransfersAsync(o, rows);
        await Task.WhenAll(managers.Select(m => m.ToLowerInvariant()).SelectMany(m => new[]
        {
            ScanAsync(o, $"nft:in:{m}", from => rpc.TransfersAsync("erc721", null, o, [m], fromBlock: from), Save),
            ScanAsync(o, $"nft:out:{m}", from => rpc.TransfersAsync("erc721", o, null, [m], fromBlock: from), Save),
        }));

        _nftTransfers.Clear();
        var refs = new Dictionary<string, PositionRef>();
        foreach (var row in (await store.NftTransfersAsync(o)).Where(r => r.TokenId is not null && _managerSet.Contains(r.Contract)))
        {
            var pos = new PositionRef(row.Contract, row.TokenId!.Value);
            refs.TryAdd(pos.Key, pos);
            _nftTransfers.GetOrAdd(pos.Key, _ => []).Add(row);
            if (row.Timestamp > 0) _timestamps[row.Block] = row.Timestamp;
        }
        return refs.Values.OrderBy(r => r.TokenId).ToList();
    }

    public async Task<IReadOnlyList<RawLiquidityLog>> LiquidityLogsAsync(string owner, IReadOnlyList<PositionRef> refs)
    {
        var o = owner.ToLowerInvariant();
        using var _ = await LockAsync(o);

        var raws = await Task.WhenAll(refs.Select(PositionAsync));
        var pairTokens = raws.OfType<RawPosition>().SelectMany(r => new[] { r.Token0, r.Token1 }).Distinct().ToList();

        // Transactions that can carry a liquidity change: NFT transfers, movements of the pair's tokens to or from the
        // wallet (deposits and collections go through routers, so the wallet is not the caller), and value-bearing sends.
        // Tokens have their own cursors, so a pair first seen today is scanned from the start while old ones are not.
        Task Save(List<AssetTransfer> rows) => store.AddHashesAsync(o, rows.Select(t => t.Hash).Distinct().ToList());
        var scans = new List<Task>
        {
            ScanAsync(o, "external:out", from => rpc.TransfersAsync("external", o, null, includeZeroValue: true, fromBlock: from), Save),
        };
        if (pairTokens.Count > 0)
        {
            scans.Add(ScanAsync(o, pairTokens, t => $"erc20:out:{t}", (ts, from) => rpc.TransfersAsync("erc20", o, null, ts, fromBlock: from), Save));
            scans.Add(ScanAsync(o, pairTokens, t => $"erc20:in:{t}", (ts, from) => rpc.TransfersAsync("erc20", null, o, ts, fromBlock: from), Save));
        }
        await Task.WhenAll(scans);

        var hashes = (await store.HashesAsync(o)).ToHashSet();
        hashes.UnionWith(_nftTransfers.Values.SelectMany(l => l.Select(t => t.Hash)));

        var decoded = await store.DecodedReceiptsAsync(hashes);
        await Task.WhenAll(hashes.Where(h => !decoded.Contains(h)).Select(LoadReceiptAsync));

        var wanted = refs.ToDictionary(r => r.Key);
        var logs = new List<RawLiquidityLog>();
        foreach (var e in await store.EventsAsync(hashes))
            if (wanted.TryGetValue(new PositionRef(e.Manager, e.TokenId).Key, out var pos))
                logs.Add(new RawLiquidityLog(e.Kind, pos, e.Amount0, e.Amount1, e.Block, e.Hash, e.LogIndex));
        return logs;
    }

    private async Task LoadReceiptAsync(string hash)
    {
        var receipt = await rpc.CallAsync("eth_getTransactionReceipt", hash);
        var events = new List<StoredEvent>();
        if (receipt.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var block = long.Parse(receipt.GetProperty("blockNumber").GetString()![2..], NumberStyles.HexNumber);
            foreach (var log in receipt.GetProperty("logs").EnumerateArray())
            {
                var address = log.GetProperty("address").GetString()!.ToLowerInvariant();
                if (!_managerSet.Contains(address)) continue;
                var topics = log.GetProperty("topics").EnumerateArray().Select(t => t.GetString()!.ToLowerInvariant()).ToList();
                if (topics.Count < 2 || !EventTopics.TryGetValue(topics[0], out var kind)) continue;
                // Increase and Decrease data: liquidity, amount0, amount1 (..). Collect data: recipient, amount0, amount1.
                var w = Abi.Words(log.GetProperty("data").GetString()!);
                if (w.Count < 3) continue;
                events.Add(new StoredEvent(
                    hash, int.Parse(log.GetProperty("logIndex").GetString()![2..], NumberStyles.HexNumber), address,
                    Abi.ToUInt(topics[1][2..]), kind, Abi.ToUInt(w[1]), Abi.ToUInt(w[2]), block));
            }
            await store.AddReceiptAsync(hash, events);
        }
        // A null receipt means the node does not know the transaction (yet): leave it undecoded so the next refresh retries.
    }

    public async Task<IReadOnlyDictionary<long, long>> BlockTimestampsAsync(IReadOnlyCollection<long> blocks)
    {
        var missing = blocks.Where(b => !_timestamps.ContainsKey(b)).ToList();
        foreach (var (b, t) in await store.TimestampsAsync(missing)) _timestamps[b] = t;

        var fetched = new ConcurrentBag<KeyValuePair<long, long>>();
        await Task.WhenAll(missing.Where(b => !_timestamps.ContainsKey(b)).Select(async b =>
        {
            var block = await rpc.CallAsync("eth_getBlockByNumber", "0x" + b.ToString("x"), false);
            var ts = long.Parse(block.GetProperty("timestamp").GetString()![2..], NumberStyles.HexNumber);
            _timestamps[b] = ts;
            fetched.Add(KeyValuePair.Create(b, ts));
        }));
        await store.AddTimestampsAsync(fetched.ToList());
        return blocks.ToDictionary(b => b, b => _timestamps[b]);
    }

    public Task<RawPosition?> PositionAsync(PositionRef position) =>
        _positions.GetOrAdd(position.Key, _ => LoadPositionAsync(position));

    private async Task<RawPosition?> LoadPositionAsync(PositionRef position)
    {
        var data = Abi.Call("positions(uint256)", Abi.Word(position.TokenId));
        try { return DecodePosition(await rpc.EthCallAsync(position.Manager, data), burned: false); }
        catch (ChainRpcException e) when (e.IsRevert) { /* burned: fall through to the block before the burn */ }

        // The wallet may have burned it directly (a transfer to zero) or handed it to a contract that burned it,
        // so fall back to the last block the wallet saw the NFT move.
        var moves = _nftTransfers.GetValueOrDefault(position.Key)?.OrderBy(t => t.Block).ToList();
        var burn = moves?.LastOrDefault(t => t.To == Zero) ?? moves?.LastOrDefault();
        if (burn is null) return null;
        try { return DecodePosition(await rpc.EthCallAsync(position.Manager, data, block: burn.Block - 1), burned: true); }
        catch (ChainRpcException) { return null; }
    }

    private static RawPosition DecodePosition(string hex, bool burned)
    {
        var w = Abi.Words(hex);
        if (w.Count < 8) throw new ChainRpcException("positions() returned no data", 3);
        return new RawPosition(
            Abi.ToAddress(w[2]), Abi.ToAddress(w[3]), (int)Abi.ToInt(w[4]), (int)Abi.ToInt(w[5]), (int)Abi.ToInt(w[6]),
            burned ? BigInteger.Zero : Abi.ToUInt(w[7]), burned);
    }

    public async Task<string?> OwnerOfAsync(PositionRef position)
    {
        try
        {
            var w = Abi.Words(await rpc.EthCallAsync(position.Manager, Abi.Call("ownerOf(uint256)", Abi.Word(position.TokenId))));
            return w.Count > 0 ? Abi.ToAddress(w[0]) : null;
        }
        catch (ChainRpcException e) when (e.IsRevert) { return null; }
    }

    public async Task<string> PoolAsync(string manager, string token0, string token1, int tickSpacing)
    {
        var factory = await _factories.GetOrAdd(manager.ToLowerInvariant(), async m =>
            Abi.ToAddress(Abi.Words(await rpc.EthCallAsync(m, Abi.Call("factory()")))[0]));
        var data = Abi.Call("getPool(address,address,int24)", Abi.AddressWord(token0), Abi.AddressWord(token1),
            Abi.Word(new BigInteger(tickSpacing)));
        return Abi.ToAddress(Abi.Words(await rpc.EthCallAsync(factory, data))[0]);
    }

    public async Task<Slot0> Slot0Async(string pool)
    {
        var w = Abi.Words(await rpc.EthCallAsync(pool, Abi.Call("slot0()")));
        return new Slot0(Abi.ToUInt(w[0]), (int)Abi.ToInt(w[1]));
    }

    public async Task<string> GaugeAsync(string pool) =>
        Abi.ToAddress(Abi.Words(await rpc.EthCallAsync(pool, Abi.Call("gauge()")))[0]);

    public async Task<bool> StakedContainsAsync(string gauge, string owner, BigInteger tokenId)
    {
        var data = Abi.Call("stakedContains(address,uint256)", Abi.AddressWord(owner), Abi.Word(tokenId));
        return !Abi.ToUInt(Abi.Words(await rpc.EthCallAsync(gauge, data))[0]).IsZero;
    }

    public async Task<BigInteger> EarnedAeroAsync(string gauge, string owner, BigInteger tokenId)
    {
        // Current gauges take a list of token IDs; the original ones took a single ID.
        var list = Abi.Call("earned(address,uint256[])", Abi.AddressWord(owner), Abi.Word(64), Abi.Word(1), Abi.Word(tokenId));
        try { return Abi.ToUInt(Abi.Words(await rpc.EthCallAsync(gauge, list))[0]); }
        catch (ChainRpcException e) when (e.IsRevert)
        {
            var single = Abi.Call("earned(address,uint256)", Abi.AddressWord(owner), Abi.Word(tokenId));
            return Abi.ToUInt(Abi.Words(await rpc.EthCallAsync(gauge, single))[0]);
        }
    }

    public async Task<(BigInteger Amount0, BigInteger Amount1)> PendingFeesAsync(string owner, PositionRef position)
    {
        try
        {
            var data = Abi.Call("collect((uint256,address,uint128,uint128))",
                Abi.Word(position.TokenId), Abi.AddressWord(owner), Abi.Word(MaxU128), Abi.Word(MaxU128));
            var w = Abi.Words(await rpc.EthCallAsync(position.Manager, data, from: owner));
            return (Abi.ToUInt(w[0]), Abi.ToUInt(w[1]));
        }
        catch (ChainRpcException) { return (0, 0); }
    }

    public async Task<TokenMeta> TokenAsync(string address)
    {
        var key = address.ToLowerInvariant();
        if (_tokens.TryGetValue(key, out var hit)) return hit;
        var symbol = Abi.DecodeString(await rpc.EthCallAsync(address, Abi.Call("symbol()")));
        var decimals = (int)Abi.ToUInt(Abi.Words(await rpc.EthCallAsync(address, Abi.Call("decimals()")))[0]);
        return _tokens[key] = new TokenMeta(symbol, decimals);
    }
}
