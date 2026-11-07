using LpDashboard.Application.Abstractions;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Borrow;

/// <summary>
/// Keeps one wallet's Aave account, reserves and daily balances up to date. Each run reads the account now and fills in at
/// most <see cref="MaxDaysPerRun"/> missing days of history (oldest first), so a long history catches up over a few runs.
/// Use a fresh DI scope per call.
/// </summary>
public sealed class LendingSyncService(
    ILendingRepository lending,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<LendingSyncService> logger)
{
    public const int MaxDaysPerRun = 120;

    /// <summary>When the wallet's first Aave day cannot be found, history goes back this many days.</summary>
    public const int FallbackDays = 90;

    private const string FallbackNotePrefix = "The wallet's first";

    public async Task<IReadOnlyList<string>> SyncAsync(
        ILendingProvider provider, string owner, CancellationToken ct, IProgress<ProviderProgress>? progress = null)
    {
        owner = owner.ToLowerInvariant();
        var errors = new List<string>();
        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var chain = provider.Chain;

        progress?.Report(new ProviderProgress("Reading the account", 0, 0));
        var state = await provider.GetAccountAsync(owner, ct);
        if (state is null) return errors;

        var account = await lending.GetAccountAsync(chain, owner, ct);
        if (account is null)
        {
            // A wallet that never used the market leaves no trace.
            if (state.TotalCollateralUsd == 0 && state.TotalDebtUsd == 0 && state.Reserves.Count == 0) return errors;
            account = new LendingAccount { Chain = chain, Owner = owner };
            lending.Add(account);
        }
        account.TotalCollateralUsd = state.TotalCollateralUsd;
        account.TotalDebtUsd = state.TotalDebtUsd;
        account.LiquidationThreshold = state.LiquidationThreshold;
        account.HealthFactor = state.HealthFactor;
        account.UpdatedAt = now;

        var reserves = (await lending.ListReservesAsync(chain, owner, ct)).ToDictionary(r => r.Reserve, StringComparer.OrdinalIgnoreCase);
        UpsertReserves(chain, owner, state, reserves, now);

        var days = (await lending.ListDaysAsync(chain, owner, ct))
            .GroupBy(d => d.Reserve, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToDictionary(d => d.Date), StringComparer.OrdinalIgnoreCase);

        try
        {
            await BackfillAsync(provider, owner, account, state, reserves, days, today, ct, progress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Aave history for {Owner} could not be read", owner);
            account.HistoryNote = $"History could not be read: {ex.Message}";
            errors.Add($"{provider.Market} history for {owner}: {ex.Message}");
        }

        WriteToday(chain, owner, state, days, today);
        progress?.Report(new ProviderProgress("Saving", 0, 0));
        await unitOfWork.SaveChangesAsync(ct);
        return errors;
    }

    private void UpsertReserves(
        Domain.Chain chain, string owner, LendingAccountState state, Dictionary<string, LendingReserve> existing, DateTime now)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in state.Reserves.Where(r => r.Supplied > 0 || r.Borrowed > 0))
        {
            seen.Add(r.Reserve);
            if (!existing.TryGetValue(r.Reserve, out var row))
            {
                row = new LendingReserve { Chain = chain, Owner = owner, Reserve = r.Reserve };
                lending.Add(row);
                existing[r.Reserve] = row;
            }
            row.Symbol = r.Symbol;
            row.Decimals = r.Decimals;
            row.PriceKey = r.PriceKey;
            row.SuppliedTokens = r.Supplied;
            row.BorrowedTokens = r.Borrowed;
            row.SupplyAprPercent = r.SupplyAprPercent;
            row.BorrowAprPercent = r.BorrowAprPercent;
            row.IsCollateral = r.IsCollateral;
            row.UpdatedAt = now;
        }

        // A reserve that is empty now stays as a row (its history still needs the symbol and price key) with zero balances.
        foreach (var row in existing.Values.Where(r => !seen.Contains(r.Reserve)))
        {
            row.SuppliedTokens = 0;
            row.BorrowedTokens = 0;
            row.UpdatedAt = now;
        }
    }

    private async Task BackfillAsync(
        ILendingProvider provider,
        string owner,
        LendingAccount account,
        LendingAccountState state,
        Dictionary<string, LendingReserve> reserves,
        Dictionary<string, Dictionary<DateOnly, LendingDay>> days,
        DateOnly today,
        CancellationToken ct,
        IProgress<ProviderProgress>? progress)
    {
        var yesterday = today.AddDays(-1);
        var reserveSet = new HashSet<string>(state.Reserves.Where(r => r.Supplied > 0 || r.Borrowed > 0).Select(r => r.Reserve), StringComparer.OrdinalIgnoreCase);
        foreach (var r in reserves.Keys) reserveSet.Add(r);

        // A fallback start (the first day could not be found) is retried every run: the failure may have been temporary or
        // fixed since (RPC plan, provider support), and then history is re-read from the true first day.
        var fallbackStart = account.HistoryNote?.StartsWith(FallbackNotePrefix, StringComparison.Ordinal) == true;
        if (account.HistoryFrom is null || fallbackStart)
        {
            var activity = await provider.FindActivityAsync(owner, ct);
            if (activity is not null)
            {
                if (account.HistoryFrom is null || activity.FirstDay < account.HistoryFrom)
                {
                    account.HistoryFrom = activity.FirstDay;
                    account.HistoryThrough = null;
                }
                foreach (var r in activity.Reserves) reserveSet.Add(r);
                account.HistoryNote = null;
            }
            else if (account.HistoryFrom is null)
            {
                account.HistoryFrom = today.AddDays(-FallbackDays);
                account.HistoryNote = $"{FallbackNotePrefix} {provider.Market} day could not be found, so history covers the last {FallbackDays} days.";
            }
        }

        var next = account.HistoryThrough is { } through ? through.AddDays(1) : account.HistoryFrom!.Value;
        if (next > yesterday) return;
        var last = new DateOnly[] { yesterday, next.AddDays(MaxDaysPerRun - 1) }.Min();

        var history = await provider.GetHistoryAsync(owner, reserveSet, next, last, ct, progress);

        // Previous-day figures per reserve, to turn index growth into yield and interest.
        var previous = days.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Values.Where(d => d.Date < next).OrderByDescending(d => d.Date).FirstOrDefault(),
            StringComparer.OrdinalIgnoreCase);

        var firstFailed = history.Failed.Count > 0 ? history.Failed.Min() : (DateOnly?)null;
        foreach (var day in history.Days.Where(d => firstFailed is null || d.Date < firstFailed).OrderBy(d => d.Date))
        {
            previous.TryGetValue(day.Reserve, out var prev);
            var had = prev is { SuppliedTokens: > 0 } or { BorrowedTokens: > 0 };
            if (day.Supplied <= 0 && day.Borrowed <= 0 && !had) continue;

            if (!days.TryGetValue(day.Reserve, out var byDate)) days[day.Reserve] = byDate = [];
            // A day that was open at an earlier sync already has a partial row; its close replaces it.
            if (!byDate.TryGetValue(day.Date, out var row))
            {
                row = new LendingDay { Chain = provider.Chain, Owner = owner, Reserve = day.Reserve, Date = day.Date };
                lending.Add(row);
                byDate[day.Date] = row;
            }
            row.SuppliedTokens = day.Supplied;
            row.BorrowedTokens = day.Borrowed;
            row.SupplyIndex = day.SupplyIndex;
            row.BorrowIndex = day.BorrowIndex;
            row.IsCollateral = day.IsCollateral;
            row.YieldTokens = prev is null ? 0 : AaveAccrual.Accrued(prev.SuppliedTokens, prev.SupplyIndex, day.SupplyIndex);
            row.InterestTokens = prev is null ? 0 : AaveAccrual.Accrued(prev.BorrowedTokens, prev.BorrowIndex, day.BorrowIndex);
            previous[day.Reserve] = row;

            if (!reserves.ContainsKey(day.Reserve))
            {
                var placeholder = new LendingReserve
                {
                    Chain = provider.Chain, Owner = owner, Reserve = day.Reserve, Symbol = day.Symbol, Decimals = day.Decimals,
                    PriceKey = day.PriceKey, UpdatedAt = time.GetUtcNow().UtcDateTime
                };
                lending.Add(placeholder);
                reserves[day.Reserve] = placeholder;
            }
        }

        if (firstFailed is { } failed)
        {
            account.HistoryThrough = failed > next ? failed.AddDays(-1) : account.HistoryThrough;
            account.HistoryNote = history.Note ?? "Some days could not be read.";
            // Nothing could be read at all: treat history as starting today rather than retrying the same days forever.
            if (account.HistoryThrough is null) account.HistoryFrom = today;
        }
        else
        {
            account.HistoryThrough = last;
            if (account.HistoryNote?.StartsWith("History could not") == true) account.HistoryNote = null;
        }
    }

    /// <summary>Today's row per reserve from the live read; today is still open, so it is rewritten on every sync.</summary>
    private void WriteToday(
        Domain.Chain chain, string owner, LendingAccountState state, Dictionary<string, Dictionary<DateOnly, LendingDay>> days, DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        var live = state.Reserves.ToDictionary(r => r.Reserve, StringComparer.OrdinalIgnoreCase);
        var keys = days.Keys.Concat(live.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var key in keys)
        {
            live.TryGetValue(key, out var now);
            days.TryGetValue(key, out var byDate);
            var prev = byDate?.Values.Where(d => d.Date < today).OrderByDescending(d => d.Date).FirstOrDefault();
            var had = prev is { SuppliedTokens: > 0 } or { BorrowedTokens: > 0 };
            if ((now is null || (now.Supplied <= 0 && now.Borrowed <= 0)) && !had) continue;

            LendingDay? row = null;
            byDate?.TryGetValue(today, out row);
            if (row is null)
            {
                row = new LendingDay { Chain = chain, Owner = owner, Reserve = now?.Reserve ?? key, Date = today };
                lending.Add(row);
            }

            row.SuppliedTokens = now?.Supplied ?? 0;
            row.BorrowedTokens = now?.Borrowed ?? 0;
            row.SupplyIndex = now?.SupplyIndex ?? prev?.SupplyIndex ?? 0;
            row.BorrowIndex = now?.BorrowIndex ?? prev?.BorrowIndex ?? 0;
            row.IsCollateral = now?.IsCollateral ?? prev?.IsCollateral ?? false;
            // Only count a full day of accrual against yesterday's close; without one (a new reserve) nothing is assumed.
            var from = prev is not null && prev.Date == yesterday ? prev : null;
            row.YieldTokens = from is null ? 0 : AaveAccrual.Accrued(from.SuppliedTokens, from.SupplyIndex, row.SupplyIndex);
            row.InterestTokens = from is null ? 0 : AaveAccrual.Accrued(from.BorrowedTokens, from.BorrowIndex, row.BorrowIndex);
        }
    }
}
