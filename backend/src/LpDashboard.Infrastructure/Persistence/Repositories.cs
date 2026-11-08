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

internal sealed class AuthRepository(AppDbContext db) : IAuthRepository
{
    public Task<AuthCredential?> GetCredentialAsync(CancellationToken ct) =>
        db.AuthCredentials.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

    public void Add(AuthCredential credential) => db.AuthCredentials.Add(credential);

    public Task<AuthSession?> FindSessionAsync(string tokenHash, CancellationToken ct) =>
        db.AuthSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public void Add(AuthSession session) => db.AuthSessions.Add(session);

    public void Remove(AuthSession session) => db.AuthSessions.Remove(session);

    public Task RemoveSessionsAsync(int? exceptId, CancellationToken ct) =>
        db.AuthSessions.Where(s => exceptId == null || s.Id != exceptId).ExecuteDeleteAsync(ct);

    public Task RemoveExpiredSessionsAsync(DateTime now, CancellationToken ct) =>
        db.AuthSessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(ct);
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

    public Task<List<SnapshotLite>> ListLiteAsync(IReadOnlyCollection<int> positionIds, CancellationToken ct) =>
        db.PositionSnapshots.AsNoTracking()
            .Where(s => positionIds.Contains(s.PositionId))
            .OrderBy(s => s.Timestamp)
            .Select(s => new SnapshotLite(s.PositionId, s.Timestamp, s.CurrentTick, s.ValueUsd, s.UncollectedUsd))
            .ToListAsync(ct);

    public Task<List<PositionSnapshot>> ListForPositionsAsync(IReadOnlyCollection<int> positionIds, CancellationToken ct) =>
        db.PositionSnapshots.AsNoTracking()
            .Where(s => positionIds.Contains(s.PositionId))
            .OrderBy(s => s.Timestamp)
            .ToListAsync(ct);

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

internal sealed class ManualLoanRepository(AppDbContext db) : IManualLoanRepository
{
    public Task<List<ManualLoan>> ListAsync(IReadOnlyCollection<string> owners, CancellationToken ct) =>
        db.ManualLoans.AsNoTracking()
            .Include(l => l.Terms)
            .Include(l => l.Collateral)
            .Where(l => owners.Contains(l.Owner))
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<ManualLoan?> GetAsync(int id, CancellationToken ct) =>
        db.ManualLoans.Include(l => l.Terms).Include(l => l.Collateral).AsSplitQuery().FirstOrDefaultAsync(l => l.Id == id, ct);

    public void Add(ManualLoan loan) => db.ManualLoans.Add(loan);

    public void Remove(ManualLoan loan) => db.ManualLoans.Remove(loan);
}

internal sealed class LendingRepository(AppDbContext db) : ILendingRepository
{
    // Tracked: the sync edits these rows in place.
    public Task<LendingAccount?> GetAccountAsync(Chain chain, string owner, CancellationToken ct) =>
        db.LendingAccounts.FirstOrDefaultAsync(a => a.Chain == chain && a.Owner == owner, ct);

    public Task<List<LendingAccount>> ListAccountsAsync(IReadOnlyCollection<string> owners, CancellationToken ct) =>
        db.LendingAccounts.AsNoTracking().Where(a => owners.Contains(a.Owner)).ToListAsync(ct);

    public Task<List<LendingReserve>> ListReservesAsync(Chain chain, string owner, CancellationToken ct) =>
        db.LendingReserves.Where(r => r.Chain == chain && r.Owner == owner).ToListAsync(ct);

    public Task<List<LendingReserve>> ListReservesAsync(IReadOnlyCollection<string> owners, CancellationToken ct) =>
        db.LendingReserves.AsNoTracking().Where(r => owners.Contains(r.Owner)).ToListAsync(ct);

    public Task<List<LendingDay>> ListDaysAsync(Chain chain, string owner, CancellationToken ct) =>
        db.LendingDays.Where(d => d.Chain == chain && d.Owner == owner).ToListAsync(ct);

    public Task<List<LendingDay>> ListDaysAsync(IReadOnlyCollection<string> owners, DateOnly from, CancellationToken ct) =>
        db.LendingDays.AsNoTracking().Where(d => owners.Contains(d.Owner) && d.Date >= from).OrderBy(d => d.Date).ToListAsync(ct);

    public void Add(LendingAccount account) => db.LendingAccounts.Add(account);
    public void Add(LendingReserve reserve) => db.LendingReserves.Add(reserve);
    public void Add(LendingDay day) => db.LendingDays.Add(day);
    public void Remove(LendingReserve reserve) => db.LendingReserves.Remove(reserve);
}

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
