using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Infrastructure.Persistence;

internal sealed class RiskRepository(AppDbContext db) : IRiskRepository
{
    public Task<RiskCheck?> GetLatestAsync(RiskSubjectKind kind, Chain chain, string subject, CancellationToken ct) =>
        db.RiskChecks.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Kind == kind && c.Chain == chain && c.Subject == subject && c.IsLatest, ct);

    public Task<List<RiskCheck>> ListLatestAsync(RiskSubjectKind kind, Chain chain, IReadOnlyCollection<string> subjects, CancellationToken ct) =>
        db.RiskChecks.AsNoTracking()
            .Where(c => c.Kind == kind && c.Chain == chain && c.IsLatest && subjects.Contains(c.Subject))
            .ToListAsync(ct);

    public Task<List<RiskCheck>> ListLatestPoolsByExplorerIdAsync(Chain chain, IReadOnlyCollection<string> explorerIds, CancellationToken ct) =>
        db.RiskChecks.AsNoTracking()
            .Where(c => c.Kind == RiskSubjectKind.Pool && c.Chain == chain && c.IsLatest && c.ExplorerId != null && explorerIds.Contains(c.ExplorerId))
            .ToListAsync(ct);

    public Task<List<RiskCheck>> ListRecentChangesAsync(Chain chain, IReadOnlyCollection<string>? subjects, int take, CancellationToken ct)
    {
        var query = db.RiskChecks.AsNoTracking().Where(c => c.Chain == chain && c.ChangesJson != "[]");
        if (subjects is not null) query = query.Where(c => subjects.Contains(c.Subject));
        return query.OrderByDescending(c => c.CheckedAt).Take(take).ToListAsync(ct);
    }

    public async Task AddCheckAsync(RiskCheck check, CancellationToken ct)
    {
        var previous = await db.RiskChecks
            .Where(c => c.Kind == check.Kind && c.Chain == check.Chain && c.Subject == check.Subject && c.IsLatest)
            .ToListAsync(ct);
        foreach (var row in previous) row.IsLatest = false;
        check.IsLatest = true;
        db.RiskChecks.Add(check);
    }

    public async Task PruneChecksAsync(RiskSubjectKind kind, Chain chain, string subject, int keep, CancellationToken ct)
    {
        var stale = await db.RiskChecks.AsNoTracking()
            .Where(c => c.Kind == kind && c.Chain == chain && c.Subject == subject)
            .OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
            .Skip(Math.Max(1, keep))
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (stale.Count > 0) await db.RiskChecks.Where(c => stale.Contains(c.Id)).ExecuteDeleteAsync(ct);
    }

    public Task<List<StarredPool>> ListStarsAsync(Chain chain, CancellationToken ct) =>
        db.StarredPools.AsNoTracking().Where(s => s.Chain == chain).OrderBy(s => s.CreatedAt).ToListAsync(ct);

    // Tracked: callers edit (PoolAddress) or remove the row.
    public Task<StarredPool?> FindStarAsync(Chain chain, string explorerId, CancellationToken ct) =>
        db.StarredPools.FirstOrDefaultAsync(s => s.Chain == chain && s.ExplorerId == explorerId, ct);

    public void Add(StarredPool star) => db.StarredPools.Add(star);

    public void Remove(StarredPool star) => db.StarredPools.Remove(star);

    public void Add(PoolLiquiditySnapshot snapshot) => db.PoolLiquiditySnapshots.Add(snapshot);

    public Task<PoolLiquiditySnapshot?> GetSnapshotAtOrBeforeAsync(Chain chain, string pool, DateTime at, TimeSpan maxAge, CancellationToken ct)
    {
        var earliest = at - maxAge;
        return db.PoolLiquiditySnapshots.AsNoTracking()
            .Where(s => s.Chain == chain && s.PoolAddress == pool && s.At <= at && s.At >= earliest)
            .OrderByDescending(s => s.At)
            .FirstOrDefaultAsync(ct);
    }

    public Task<PoolLiquiditySnapshot?> GetLatestSnapshotAsync(Chain chain, string pool, CancellationToken ct) =>
        db.PoolLiquiditySnapshots.AsNoTracking()
            .Where(s => s.Chain == chain && s.PoolAddress == pool)
            .OrderByDescending(s => s.At)
            .FirstOrDefaultAsync(ct);

    public Task PruneSnapshotsAsync(DateTime olderThan, CancellationToken ct) =>
        db.PoolLiquiditySnapshots.Where(s => s.At < olderThan).ExecuteDeleteAsync(ct);

    public Task<RiskAlertState?> FindOpenAlertAsync(RiskSubjectKind kind, Chain chain, string subject, string code, CancellationToken ct) =>
        db.RiskAlertStates.FirstOrDefaultAsync(
            a => a.Kind == kind && a.Chain == chain && a.Subject == subject && a.Code == code && a.ClearedAt == null, ct);

    public Task<List<RiskAlertState>> ListOpenAlertsAsync(RiskSubjectKind kind, Chain chain, string subject, CancellationToken ct) =>
        db.RiskAlertStates.Where(a => a.Kind == kind && a.Chain == chain && a.Subject == subject && a.ClearedAt == null).ToListAsync(ct);

    public void Add(RiskAlertState state) => db.RiskAlertStates.Add(state);
}
