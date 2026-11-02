using System.Numerics;
using LpLedger.Core.Models;

namespace LpLedger.Infrastructure.Chain;

/// <summary>A position-manager event decoded from a transaction receipt.</summary>
public sealed record StoredEvent(
    string Hash, int LogIndex, string Manager, BigInteger TokenId, EventKind Kind, BigInteger Amount0, BigInteger Amount1, long Block);

/// <summary>
/// What the chain reader has already learned about a wallet, so a refresh asks the node only about new blocks.
/// Everything here is immutable history except the cursors, which record how far each query has been scanned.
/// All wallet addresses are lowercase.
/// </summary>
public interface IScanStore
{
    /// <summary>Last block the named query has been scanned through, or null if it never ran.</summary>
    Task<long?> CursorAsync(string wallet, string key);
    Task SetCursorAsync(string wallet, string key, long block);

    Task<IReadOnlyList<AssetTransfer>> NftTransfersAsync(string wallet);
    Task AddNftTransfersAsync(string wallet, IReadOnlyCollection<AssetTransfer> rows);

    /// <summary>Hashes of transactions that may carry a liquidity change for the wallet.</summary>
    Task<IReadOnlySet<string>> HashesAsync(string wallet);
    Task AddHashesAsync(string wallet, IReadOnlyCollection<string> hashes);

    /// <summary>Which of the hashes already have a decoded receipt (a receipt with no manager events still counts).</summary>
    Task<IReadOnlySet<string>> DecodedReceiptsAsync(IReadOnlyCollection<string> hashes);
    Task AddReceiptAsync(string hash, IReadOnlyCollection<StoredEvent> events);
    Task<IReadOnlyList<StoredEvent>> EventsAsync(IReadOnlyCollection<string> hashes);

    Task<IReadOnlyDictionary<long, long>> TimestampsAsync(IReadOnlyCollection<long> blocks);
    Task AddTimestampsAsync(IReadOnlyCollection<KeyValuePair<long, long>> blocks);
}

/// <summary>Keeps history for the life of the process. Used when no database is configured.</summary>
public sealed class MemoryScanStore : IScanStore
{
    private readonly object _lock = new();
    private readonly Dictionary<(string, string), long> _cursors = [];
    private readonly Dictionary<string, HashSet<AssetTransfer>> _nft = [];
    private readonly Dictionary<string, HashSet<string>> _hashes = [];
    private readonly Dictionary<string, List<StoredEvent>> _receipts = [];
    private readonly Dictionary<long, long> _times = [];

    public Task<long?> CursorAsync(string wallet, string key)
    {
        lock (_lock) return Task.FromResult(_cursors.TryGetValue((wallet, key), out var b) ? (long?)b : null);
    }

    public Task SetCursorAsync(string wallet, string key, long block)
    {
        lock (_lock) _cursors[(wallet, key)] = block;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AssetTransfer>> NftTransfersAsync(string wallet)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<AssetTransfer>>(_nft.GetValueOrDefault(wallet)?.ToList() ?? []);
    }

    public Task AddNftTransfersAsync(string wallet, IReadOnlyCollection<AssetTransfer> rows)
    {
        lock (_lock)
        {
            if (!_nft.TryGetValue(wallet, out var set)) _nft[wallet] = set = [];
            set.UnionWith(rows);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> HashesAsync(string wallet)
    {
        lock (_lock) return Task.FromResult<IReadOnlySet<string>>(_hashes.GetValueOrDefault(wallet)?.ToHashSet() ?? []);
    }

    public Task AddHashesAsync(string wallet, IReadOnlyCollection<string> hashes)
    {
        lock (_lock)
        {
            if (!_hashes.TryGetValue(wallet, out var set)) _hashes[wallet] = set = [];
            set.UnionWith(hashes);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> DecodedReceiptsAsync(IReadOnlyCollection<string> hashes)
    {
        lock (_lock) return Task.FromResult<IReadOnlySet<string>>(hashes.Where(_receipts.ContainsKey).ToHashSet());
    }

    public Task AddReceiptAsync(string hash, IReadOnlyCollection<StoredEvent> events)
    {
        lock (_lock) _receipts.TryAdd(hash, events.ToList());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredEvent>> EventsAsync(IReadOnlyCollection<string> hashes)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<StoredEvent>>(
                hashes.SelectMany(h => _receipts.GetValueOrDefault(h) ?? []).ToList());
    }

    public Task<IReadOnlyDictionary<long, long>> TimestampsAsync(IReadOnlyCollection<long> blocks)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyDictionary<long, long>>(
                blocks.Where(_times.ContainsKey).ToDictionary(b => b, b => _times[b]));
    }

    public Task AddTimestampsAsync(IReadOnlyCollection<KeyValuePair<long, long>> blocks)
    {
        lock (_lock) foreach (var (b, t) in blocks) _times[b] = t;
        return Task.CompletedTask;
    }
}
