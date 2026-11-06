using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Metrics;

/// <summary>
/// One reward token across all positions. "Held" figures assume the claimed tokens were kept; the app cannot see
/// whether they were sold, so <see cref="ChangeSinceClaimUsd"/> answers "what would sell-on-claim have saved or cost".
/// </summary>
/// <param name="ClaimedUsd">Value of everything claimed, at the price on each claim day.</param>
/// <param name="AverageClaimPriceUsd">ClaimedUsd / ClaimedAmount.</param>
/// <param name="CurrentPriceUsd">Latest price seen on any position paying this token; null when none is known.</param>
/// <param name="ClaimedValueNowUsd">What the claimed tokens would be worth today.</param>
/// <param name="ChangeSinceClaimUsd">ClaimedValueNowUsd - ClaimedUsd; negative means selling at claim would have been better.</param>
/// <param name="PendingAmount">Rewards accrued on open positions and not claimed yet.</param>
/// <param name="HasEstimatedPrices">Some claims had no price on the day and were valued at the next known price.</param>
/// <param name="HasMissingPrices">Some claims could not be priced at all and are not in the dollar figures.</param>
public sealed record RewardTokenSummary(
    string Symbol,
    string? Address,
    int ClaimCount,
    DateTime? FirstClaimAt,
    DateTime? LastClaimAt,
    decimal ClaimedAmount,
    decimal ClaimedUsd,
    decimal? AverageClaimPriceUsd,
    decimal? CurrentPriceUsd,
    decimal? ClaimedValueNowUsd,
    decimal? ChangeSinceClaimUsd,
    decimal? ChangeSinceClaimPercent,
    decimal PendingAmount,
    decimal? PendingUsd,
    bool HasEstimatedPrices,
    bool HasMissingPrices);

public static class RewardPnlCalculator
{
    public static IReadOnlyList<RewardTokenSummary> Calculate(IEnumerable<Position> positions)
    {
        var list = positions.ToList();

        // Group by token address; fall back to the symbol for events recorded without one.
        static string Key(string? address, string? symbol) =>
            (!string.IsNullOrWhiteSpace(address) ? address : symbol ?? "?").Trim().ToLowerInvariant();

        var claims = list
            .SelectMany(p => p.Events)
            .Where(e => e.Type == PositionEventType.RewardClaim && e.RewardAmount > 0)
            .OrderBy(e => e.Timestamp)
            .ToList();

        // Latest known price and pending balance per token, from the positions that pay it.
        var current = new Dictionary<string, (decimal Price, DateTime At)>();
        var pending = new Dictionary<string, decimal>();
        foreach (var p in list)
        {
            if (p.RewardTokenAddress is null && p.RewardTokenSymbol is null) continue;
            var key = Key(p.RewardTokenAddress, p.RewardTokenSymbol);
            if (p.RewardPriceUsd is { } price && p.ValuedAt is { } at && (!current.TryGetValue(key, out var seen) || at > seen.At))
            {
                current[key] = (price, at);
            }

            if (p.Status == PositionStatus.Open && p.PendingReward > 0)
            {
                pending[key] = pending.GetValueOrDefault(key) + p.PendingReward;
            }
        }

        var tokens = claims.Select(e => Key(e.RewardTokenAddress, e.RewardTokenSymbol))
            .Concat(pending.Keys)
            .Distinct()
            .ToList();

        var result = new List<RewardTokenSummary>();
        foreach (var key in tokens)
        {
            var mine = claims.Where(e => Key(e.RewardTokenAddress, e.RewardTokenSymbol) == key).ToList();
            var symbol = mine.Select(e => e.RewardTokenSymbol).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                ?? list.Select(p => p.RewardTokenSymbol).FirstOrDefault(s => !string.IsNullOrEmpty(s) && Key(null, s) == key)
                ?? key;
            var address = mine.Select(e => e.RewardTokenAddress).FirstOrDefault(a => !string.IsNullOrEmpty(a))
                ?? list.Where(p => Key(p.RewardTokenAddress, p.RewardTokenSymbol) == key).Select(p => p.RewardTokenAddress).FirstOrDefault(a => !string.IsNullOrEmpty(a));
            var currentPrice = current.TryGetValue(key, out var c) ? c.Price : (decimal?)null;

            decimal claimedAmount = 0, claimedUsd = 0, pricedAmount = 0;
            var estimated = false;
            var unpriced = false;
            for (var i = 0; i < mine.Count; i++)
            {
                var e = mine[i];
                claimedAmount += e.RewardAmount;

                var price = e.RewardPriceUsd;
                if (price is null)
                {
                    // Freshly launched tokens have no history on the claim day: use the next price known for the same token,
                    // as the per-position figures do, with the current price as the last resort.
                    price = mine.Skip(i + 1).Select(o => o.RewardPriceUsd).FirstOrDefault(x => x is not null) ?? currentPrice;
                    if (price is null)
                    {
                        unpriced = true;
                        continue;
                    }
                    estimated = true;
                }

                claimedUsd += e.RewardAmount * price.Value;
                pricedAmount += e.RewardAmount;
            }

            // Compare like with like: only claims that have a dollar value count towards the "worth now" side.
            decimal? average = pricedAmount > 0 ? claimedUsd / pricedAmount : null;
            decimal? valueNow = currentPrice is { } cp && pricedAmount > 0 ? pricedAmount * cp : null;
            decimal? change = valueNow is { } now ? now - claimedUsd : null;

            pending.TryGetValue(key, out var pendingAmount);
            decimal? pendingUsd = currentPrice is { } pp ? pendingAmount * pp : pendingAmount == 0 ? 0m : null;

            result.Add(new RewardTokenSummary(
                symbol,
                address,
                mine.Count,
                mine.Count > 0 ? mine[0].Timestamp : null,
                mine.Count > 0 ? mine[^1].Timestamp : null,
                claimedAmount,
                claimedUsd,
                average,
                currentPrice,
                valueNow,
                change,
                change is { } ch && claimedUsd > 0 ? ch / claimedUsd * 100 : null,
                pendingAmount,
                pendingUsd,
                estimated,
                unpriced));
        }

        return result.OrderByDescending(r => r.ClaimedUsd + (r.PendingUsd ?? 0)).ToList();
    }
}
