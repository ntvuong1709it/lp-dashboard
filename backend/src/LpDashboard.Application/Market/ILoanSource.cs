using LpDashboard.Application.Borrow;

namespace LpDashboard.Application.Market;

/// <summary>The owner's active loans today. A seam so the Market watch can be tested without the Borrow data.</summary>
public interface ILoanSource
{
    Task<IReadOnlyList<LoanDto>> GetLoansAsync(IReadOnlyCollection<string> owners, TimeZoneInfo zone, CancellationToken ct);
}

/// <summary>The real thing: the same loans the Borrow page lists.</summary>
public sealed class BorrowLoanSource(BorrowQueryService borrow) : ILoanSource
{
    // The window only shapes the interest figures, which the Market watch does not use.
    private const int Days = 30;

    public Task<IReadOnlyList<LoanDto>> GetLoansAsync(IReadOnlyCollection<string> owners, TimeZoneInfo zone, CancellationToken ct) =>
        borrow.GetLoansAsync(owners, Days, zone, ct);
}
