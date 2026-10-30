using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Infrastructure.Persistence;

internal sealed class WatchedAddressRepository(AppDbContext db) : IWatchedAddressRepository
{
    public Task<List<WatchedAddress>> ListAsync(CancellationToken ct) =>
        db.WatchedAddresses.OrderBy(a => a.CreatedAt).ToListAsync(ct);

    public Task<WatchedAddress?> GetAsync(int id, CancellationToken ct) =>
        db.WatchedAddresses.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<WatchedAddress?> FindAsync(string address, CancellationToken ct) =>
        db.WatchedAddresses.FirstOrDefaultAsync(a => a.Address == address, ct);

    public void Add(WatchedAddress address) => db.WatchedAddresses.Add(address);

    public void Remove(WatchedAddress address) => db.WatchedAddresses.Remove(address);
}

internal sealed class TokenRepository(AppDbContext db) : ITokenRepository
{
    public Task<Token?> FindAsync(Chain chain, string address, CancellationToken ct) =>
        db.Tokens.FirstOrDefaultAsync(t => t.Chain == chain && t.Address == address, ct);

    public void Add(Token token) => db.Tokens.Add(token);
}

internal sealed class PoolRepository(AppDbContext db) : IPoolRepository
{
    public Task<Pool?> FindAsync(Chain chain, string address, CancellationToken ct) =>
        db.Pools.Include(p => p.Token0).Include(p => p.Token1).FirstOrDefaultAsync(p => p.Chain == chain && p.Address == address, ct);

    public void Add(Pool pool) => db.Pools.Add(pool);
}

internal sealed class PositionRepository(AppDbContext db) : IPositionRepository
{
    public Task<List<Position>> ListForOwnerAsync(Chain chain, Protocol protocol, string owner, CancellationToken ct) =>
        WithDetails(db.Positions)
            .Where(p => p.Chain == chain && p.Protocol == protocol && p.Owner == owner)
            .ToListAsync(ct);

    public Task<List<Position>> QueryAsync(PositionFilter filter, CancellationToken ct)
    {
        var query = WithDetails(db.Positions.AsNoTracking()).Where(p => filter.Owners.Contains(p.Owner));
        if (filter.Status is not null) query = query.Where(p => p.Status == filter.Status);
        if (filter.Protocol is not null) query = query.Where(p => p.Protocol == filter.Protocol);
        if (filter.Chain is not null) query = query.Where(p => p.Chain == filter.Chain);
        return query.ToListAsync(ct);
    }

    public Task<Position?> GetAsync(int id, CancellationToken ct) =>
        WithDetails(db.Positions.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, ct);

    public void Add(Position position) => db.Positions.Add(position);

    private static IQueryable<Position> WithDetails(IQueryable<Position> query) =>
        query
            .Include(p => p.Pool).ThenInclude(p => p!.Token0)
            .Include(p => p.Pool).ThenInclude(p => p!.Token1)
            .Include(p => p.Events)
            .AsSplitQuery();
}

internal sealed class PositionEventRepository(AppDbContext db) : IPositionEventRepository
{
    public Task<List<PositionEvent>> ListClaimsAsync(ClaimFilter filter, CancellationToken ct)
    {
        var query = db.PositionEvents.AsNoTracking()
            .Include(e => e.Position).ThenInclude(p => p!.Pool).ThenInclude(p => p!.Token0)
            .Include(e => e.Position).ThenInclude(p => p!.Pool).ThenInclude(p => p!.Token1)
            .Where(e => e.Type == PositionEventType.Collect || e.Type == PositionEventType.RewardClaim)
            .Where(e => filter.Owners.Contains(e.Position!.Owner));

        if (filter.From is not null) query = query.Where(e => e.Timestamp >= filter.From);
        if (filter.To is not null) query = query.Where(e => e.Timestamp < filter.To);
        return query.OrderByDescending(e => e.Timestamp).ToListAsync(ct);
    }
}

internal sealed class SnapshotRepository(AppDbContext db) : ISnapshotRepository
{
    public Task<PositionSnapshot?> GetLatestAsync(int positionId, CancellationToken ct) =>
        db.PositionSnapshots.AsNoTracking()
            .Where(s => s.PositionId == positionId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync(ct);

    public Task<List<PositionSnapshot>> ListAsync(int positionId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = db.PositionSnapshots.AsNoTracking().Where(s => s.PositionId == positionId);
        if (from is not null) query = query.Where(s => s.Timestamp >= from);
        if (to is not null) query = query.Where(s => s.Timestamp < to);
        return query.OrderBy(s => s.Timestamp).ToListAsync(ct);
    }

    public void Add(PositionSnapshot snapshot) => db.PositionSnapshots.Add(snapshot);
}

internal sealed class TokenPriceRepository(AppDbContext db) : ITokenPriceRepository
{
    public Task<List<TokenPrice>> FindAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<long> timestamps, CancellationToken ct) =>
        db.TokenPrices.AsNoTracking()
            .Where(p => keys.Contains(p.PriceKey) && timestamps.Contains(p.Timestamp))
            .ToListAsync(ct);

    public void Add(TokenPrice price) => db.TokenPrices.Add(price);
}

internal sealed class SyncCursorRepository(AppDbContext db) : ISyncCursorRepository
{
    public Task<SyncCursor?> GetAsync(Chain chain, Protocol protocol, string owner, CancellationToken ct) =>
        db.SyncCursors.FirstOrDefaultAsync(c => c.Chain == chain && c.Protocol == protocol && c.Owner == owner, ct);

    public void Add(SyncCursor cursor) => db.SyncCursors.Add(cursor);
}

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
