namespace LpDashboard.Domain.Metrics;

public sealed record TermRow(DateOnly From, decimal Principal, decimal AprPercent);

public sealed record AmountRow(DateOnly From, decimal Amount);

/// <summary>One day of a manual loan, in units of the borrowed and collateral assets.</summary>
public sealed record ManualLoanDay(
    DateOnly Date,
    bool Active,
    decimal DebtTokens,
    decimal InterestTokens,
    decimal AprPercent,
    decimal CollateralTokens);

/// <summary>
/// Daily debt, interest and collateral of a manual loan from its terms history.
/// The row in force on a day sets principal and rate; interest is simple (<c>principal * APR / 365</c> per day) and debt is the
/// row's principal plus the interest accrued since its date, so a new row restates the lender's total debt.
/// A day before the first row, or after the closing date, carries nothing.
/// </summary>
public static class ManualLoanAccrual
{
    public static IReadOnlyList<ManualLoanDay> Daily(
        IEnumerable<TermRow> terms, IEnumerable<AmountRow> collateral, DateOnly? closedOn, DateOnly from, DateOnly to)
    {
        var termList = terms.OrderBy(t => t.From).ToList();
        var collateralList = collateral.OrderBy(c => c.From).ToList();
        var days = new List<ManualLoanDay>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var term = termList.LastOrDefault(t => t.From <= day);
            if (term is null || (closedOn is { } closed && day > closed))
            {
                days.Add(new ManualLoanDay(day, false, 0, 0, 0, 0));
                continue;
            }

            var perDay = term.Principal * term.AprPercent / 100m / 365m;
            var elapsed = day.DayNumber - term.From.DayNumber;
            var amount = collateralList.LastOrDefault(c => c.From <= day)?.Amount ?? 0m;
            days.Add(new ManualLoanDay(day, true, term.Principal + perDay * elapsed, perDay, term.AprPercent, amount));
        }

        return days;
    }

    /// <summary>The loan today: the latest row at or before <paramref name="today"/>, or null when none applies or it is closed.</summary>
    public static (decimal Principal, decimal DebtTokens, decimal AprPercent)? Current(
        IEnumerable<TermRow> terms, DateOnly? closedOn, DateOnly today)
    {
        if (closedOn is { } closed && today > closed) return null;
        var term = terms.Where(t => t.From <= today).OrderBy(t => t.From).LastOrDefault();
        if (term is null) return null;
        var perDay = term.Principal * term.AprPercent / 100m / 365m;
        return (term.Principal, term.Principal + perDay * (today.DayNumber - term.From.DayNumber), term.AprPercent);
    }
}
