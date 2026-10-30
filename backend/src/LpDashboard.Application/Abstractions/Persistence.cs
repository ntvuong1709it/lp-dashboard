using LpDashboard.Domain;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Application.Abstractions;

public sealed record PositionFilter(
    IReadOnlyCollection<string> Owners,
    PositionStatus? Status = null,
    Protocol? Protocol = null,
    Chain? Chain = null);

public sealed record ClaimFilter(IReadOnlyCollection<string> Owners, DateTime? From = null, DateTime? To = null);

public interface IWatchedAddressRepository
{
    Task<List<WatchedAddress>> ListAsync(CancellationToken ct);
    Task<WatchedAddress?> GetAsync(int id, CancellationToken ct);
    Task<WatchedAddress?> FindAsync(string address, CancellationToken ct);
    void Add(WatchedAddress address);
    void Remove(WatchedAddress address);
}

public interface ITokenRepository
{
    Task<Token?> FindAsync(Chain chain, string address, CancellationToken ct);
    void Add(Token token);
}

public interface IPoolRepository
{
    Task<Pool?> FindAsync(Chain chain, string address, CancellationToken ct);
    void Add(Pool pool);
}

public interface IPositionRepository
{
    /// <summary>Includes events, pool and tokens.</summary>
    Task<List<Position>> ListForOwnerAsync(Chain chain, Protocol protocol, string owner, CancellationToken ct);

    /// <summary>Includes events, pool and tokens.</summary>
    Task<List<Position>> QueryAsync(PositionFilter filter, CancellationToken ct);

    /// <summary>Includes events, pool and tokens.</summary>
    Task<Position?> GetAsync(int id, CancellationToken ct);

    void Add(Position position);
}

public interface IPositionEventRepository
{
    /// <summary>Collect and RewardClaim events, including position, pool and tokens.</summary>
    Task<List<PositionEvent>> ListClaimsAsync(ClaimFilter filter, CancellationToken ct);
}

public interface ISnapshotRepository
{
    Task<PositionSnapshot?> GetLatestAsync(int positionId, CancellationToken ct);
    Task<List<PositionSnapshot>> ListAsync(int positionId, DateTime? from, DateTime? to, CancellationToken ct);
    Task<List<PositionSnapshot>> ListForPositionsAsync(IReadOnlyCollection<int> positionIds, CancellationToken ct);
    void Add(PositionSnapshot snapshot);
}

public interface ITokenPriceRepository
{
    Task<List<TokenPrice>> FindAsync(IReadOnlyCollection<string> keys, IReadOnlyCollection<long> timestamps, CancellationToken ct);
    void Add(TokenPrice price);
}

public interface ISyncCursorRepository
{
    Task<SyncCursor?> GetAsync(Chain chain, Protocol protocol, string owner, CancellationToken ct);
    void Add(SyncCursor cursor);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
