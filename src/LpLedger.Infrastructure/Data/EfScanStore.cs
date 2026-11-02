using LpLedger.Infrastructure.Chain;
using Microsoft.EntityFrameworkCore;

namespace LpLedger.Infrastructure.Data;

public sealed class EfScanStore(IDbContextFactory<LedgerDb> factory) : IScanStore
{
    private const int Chunk = 500;

    public async Task<long?> CursorAsync(string wallet, string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Cursors.AsNoTracking().FirstOrDefaultAsync(c => c.Wallet == wallet && c.Key == key);
        return row?.Block;
    }

    public async Task SetCursorAsync(string wallet, string key, long block)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Cursors.FindAsync(wallet, key);
        if (row is null) db.Cursors.Add(new CursorRow { Wallet = wallet, Key = key, Block = block });
        else row.Block = Math.Max(row.Block, block);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AssetTransfer>> NftTransfersAsync(string wallet)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.NftTransfers.AsNoTracking().Where(t => t.Wallet == wallet).ToListAsync();
        return rows.Select(t => new AssetTransfer(t.Hash, t.Block, t.Timestamp, t.From, t.To, t.Contract, t.TokenId)).ToList();
    }

    public async Task AddNftTransfersAsync(string wallet, IReadOnlyCollection<AssetTransfer> rows)
    {
        if (rows.Count == 0) return;
        await using var db = await factory.CreateDbContextAsync();
        var have = (await db.NftTransfers.AsNoTracking().Where(t => t.Wallet == wallet).ToListAsync())
            .Select(t => (t.Hash, t.Contract, t.TokenId, t.From, t.To)).ToHashSet();
        foreach (var t in rows.Where(t => t.TokenId is not null))
        {
            if (!have.Add((t.Hash, t.Contract, t.TokenId!.Value, t.From, t.To))) continue;
            db.NftTransfers.Add(new NftTransferRow
            {
                Wallet = wallet, Hash = t.Hash, Contract = t.Contract, TokenId = t.TokenId!.Value,
                From = t.From, To = t.To, Block = t.Block, Timestamp = t.Timestamp,
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlySet<string>> HashesAsync(string wallet)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.WalletTxs.AsNoTracking().Where(t => t.Wallet == wallet).Select(t => t.Hash).ToListAsync()).ToHashSet();
    }

    public async Task AddHashesAsync(string wallet, IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count == 0) return;
        await using var db = await factory.CreateDbContextAsync();
        var have = (await db.WalletTxs.AsNoTracking().Where(t => t.Wallet == wallet).Select(t => t.Hash).ToListAsync()).ToHashSet();
        foreach (var h in hashes.Where(have.Add)) db.WalletTxs.Add(new WalletTxRow { Wallet = wallet, Hash = h });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlySet<string>> DecodedReceiptsAsync(IReadOnlyCollection<string> hashes)
    {
        await using var db = await factory.CreateDbContextAsync();
        var found = new HashSet<string>();
        foreach (var part in hashes.Chunk(Chunk))
            found.UnionWith(await db.Receipts.AsNoTracking().Where(r => part.Contains(r.Hash)).Select(r => r.Hash).ToListAsync());
        return found;
    }

    public async Task AddReceiptAsync(string hash, IReadOnlyCollection<StoredEvent> events)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Receipts.AnyAsync(r => r.Hash == hash)) return;
        db.Receipts.Add(new ReceiptRow { Hash = hash });
        foreach (var e in events)
            db.Events.Add(new EventRow
            {
                Hash = e.Hash, LogIndex = e.LogIndex, Manager = e.Manager, TokenId = e.TokenId, Kind = e.Kind,
                Amount0 = e.Amount0, Amount1 = e.Amount1, Block = e.Block,
            });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Another wallet's lookup may have stored the same transaction first; anything else is a real failure.
            if (!await AlreadyDecodedAsync(hash)) throw;
        }
    }

    private async Task<bool> AlreadyDecodedAsync(string hash)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Receipts.AnyAsync(r => r.Hash == hash);
    }

    public async Task<IReadOnlyList<StoredEvent>> EventsAsync(IReadOnlyCollection<string> hashes)
    {
        await using var db = await factory.CreateDbContextAsync();
        var all = new List<EventRow>();
        foreach (var part in hashes.Chunk(Chunk))
            all.AddRange(await db.Events.AsNoTracking().Where(e => part.Contains(e.Hash)).ToListAsync());
        return all.Select(e => new StoredEvent(e.Hash, e.LogIndex, e.Manager, e.TokenId, e.Kind, e.Amount0, e.Amount1, e.Block)).ToList();
    }

    public async Task<IReadOnlyDictionary<long, long>> TimestampsAsync(IReadOnlyCollection<long> blocks)
    {
        await using var db = await factory.CreateDbContextAsync();
        var found = new Dictionary<long, long>();
        foreach (var part in blocks.Chunk(Chunk))
            foreach (var r in await db.BlockTimes.AsNoTracking().Where(b => part.Contains(b.Block)).ToListAsync())
                found[r.Block] = r.Timestamp;
        return found;
    }

    public async Task AddTimestampsAsync(IReadOnlyCollection<KeyValuePair<long, long>> blocks)
    {
        if (blocks.Count == 0) return;
        await using var db = await factory.CreateDbContextAsync();
        var have = await TimestampsAsync(blocks.Select(b => b.Key).ToList());
        foreach (var (b, t) in blocks.Where(b => !have.ContainsKey(b.Key)).DistinctBy(b => b.Key))
            db.BlockTimes.Add(new BlockTimeRow { Block = b, Timestamp = t });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* a concurrent lookup stored the same immutable blocks */ }
    }
}
