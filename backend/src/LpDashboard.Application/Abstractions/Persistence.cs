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

/// <summary>The few snapshot columns needed for time in range and portfolio history.</summary>
public sealed record SnapshotLite(int PositionId, DateTime Timestamp, int CurrentTick, decimal ValueUsd, decimal UncollectedUsd);

public interface ISnapshotRepository
{
    Task<List<SnapshotLite>> ListLiteAsync(IReadOnlyCollection<int> positionIds, CancellationToken ct);
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

public interface IManualLoanRepository
{
    /// <summary>Loans of these wallets, with their terms and collateral history.</summary>
    Task<List<ManualLoan>> ListAsync(IReadOnlyCollection<string> owners, CancellationToken ct);

    /// <summary>Tracked, with terms and collateral history, so it can be edited.</summary>
    Task<ManualLoan?> GetAsync(int id, CancellationToken ct);

    void Add(ManualLoan loan);
    void Remove(ManualLoan loan);
}

public interface ILendingRepository
{
    Task<LendingAccount?> GetAccountAsync(Chain chain, string owner, CancellationToken ct);
    Task<List<LendingAccount>> ListAccountsAsync(IReadOnlyCollection<string> owners, CancellationToken ct);
    Task<List<LendingReserve>> ListReservesAsync(Chain chain, string owner, CancellationToken ct);
    Task<List<LendingReserve>> ListReservesAsync(IReadOnlyCollection<string> owners, CancellationToken ct);
    Task<List<LendingDay>> ListDaysAsync(Chain chain, string owner, CancellationToken ct);
    Task<List<LendingDay>> ListDaysAsync(IReadOnlyCollection<string> owners, DateOnly from, CancellationToken ct);
    void Add(LendingAccount account);
    void Add(LendingReserve reserve);
    void Add(LendingDay day);
    void Remove(LendingReserve reserve);
}

public interface IAuthRepository
{
    /// <summary>The single credential row, tracked so it can be updated.</summary>
    Task<AuthCredential?> GetCredentialAsync(CancellationToken ct);

    void Add(AuthCredential credential);

    Task<AuthSession?> FindSessionAsync(string tokenHash, CancellationToken ct);

    void Add(AuthSession session);

    void Remove(AuthSession session);

    /// <summary>Deletes sessions immediately (not through the unit of work); keeps <paramref name="exceptId"/> if given.</summary>
    Task RemoveSessionsAsync(int? exceptId, CancellationToken ct);

    Task RemoveExpiredSessionsAsync(DateTime now, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
