using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Queries;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Application.Export;

/// <summary>Builds the CSV downloads from the same data and calculations the dashboard shows.</summary>
public sealed class ExportService(PositionQueryService queries, IPositionRepository positions)
{
    public async Task<byte[]> PositionsAsync(PositionFilter filter, CancellationToken ct)
    {
        var csv = new CsvBuilder(
            "position_id", "chain", "protocol", "token_id", "owner", "status", "staked", "pool_address", "pair", "fee_percent",
            "tick_lower", "tick_upper", "price_lower", "price_upper", "price_current", "in_range",
            "opened_at", "closed_at", "age_days", "time_in_range_percent", "time_in_range_measured_hours", "time_in_range_estimated_hours",
            "value_usd", "deposited_usd", "withdrawn_usd", "fees_earned_usd", "claimed_fees_usd", "claimed_rewards_usd",
            "unclaimed_usd", "gas_usd", "pnl_usd", "pnl_percent", "impermanent_loss_usd", "pnl_vs_hodl_usd",
            "fee_apr_percent", "missing_prices", "estimated_prices");

        foreach (var p in await queries.ListAsync(filter, ct))
        {
            var m = p.Metrics;
            csv.AddRow(
                p.Id, p.Chain, p.Protocol, p.TokenId, p.Owner, p.Status, p.IsStaked, p.PoolAddress,
                $"{p.Token0?.Symbol ?? "?"}/{p.Token1?.Symbol ?? "?"}", p.Fee is { } fee ? fee / 10000m : null,
                p.TickLower, p.TickUpper, p.Range?.PriceLower, p.Range?.PriceUpper, p.Range?.PriceCurrent, p.Range?.InRange,
                p.OpenedAt, p.ClosedAt, m.AgeDays, p.TimeInRange?.Percent, p.TimeInRange?.MeasuredHours, p.TimeInRange?.EstimatedHours,
                m.CurrentValueUsd, m.DepositedUsd, m.WithdrawnUsd, m.FeesEarnedUsd, m.CollectedFeesUsd, m.ClaimedRewardsUsd,
                m.UncollectedFeesUsd + m.PendingRewardsUsd, m.GasUsd, m.PnlUsd, m.PnlPercent, m.ImpermanentLossUsd, m.PnlVsHodlUsd,
                m.FeeAprPercent, m.HasMissingPrices, m.HasEstimatedPrices);
        }
        return csv.ToBytes();
    }

    public async Task<byte[]> EventsAsync(PositionFilter filter, CancellationToken ct)
    {
        var csv = new CsvBuilder(
            "timestamp", "type", "position_id", "chain", "protocol", "token_id", "owner", "pair", "tx_hash", "block",
            "amount0", "amount1", "principal0", "principal1", "fee0", "fee1", "reward_token", "reward_amount",
            "price0_usd", "price1_usd", "reward_price_usd", "value_usd", "gas_usd");

        var rows = (await positions.QueryAsync(filter, ct))
            .SelectMany(p => p.Events.Select(e => (Position: p, Event: e)))
            .OrderBy(x => x.Event.Timestamp)
            .ThenBy(x => x.Event.BlockNumber)
            .ThenBy(x => x.Event.LogIndex);

        foreach (var (p, e) in rows)
        {
            var dto = PositionQueryService.ToEventDto(e);
            csv.AddRow(
                dto.Timestamp, dto.Type, p.Id, p.Chain, p.Protocol, p.TokenId, p.Owner,
                $"{p.Pool?.Token0?.Symbol ?? "?"}/{p.Pool?.Token1?.Symbol ?? "?"}", dto.TxHash, dto.BlockNumber,
                dto.Amount0, dto.Amount1, dto.Principal0, dto.Principal1, dto.Fee0, dto.Fee1,
                dto.RewardTokenSymbol, dto.RewardAmount, dto.Price0Usd, dto.Price1Usd, dto.RewardPriceUsd, dto.ValueUsd, dto.GasUsd);
        }
        return csv.ToBytes();
    }

    /// <summary>One row per fee or reward claim, tagged with the (time zone local) week it falls in.</summary>
    public async Task<byte[]> FeeClaimsAsync(ClaimFilter filter, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var csv = new CsvBuilder(
            "week_start", "timestamp", "position_id", "token_id", "protocol", "pair", "type", "tx_hash",
            "fee0", "token0", "fee1", "token1", "reward_amount", "reward_token",
            "fees_usd", "rewards_usd", "total_usd", "missing_prices");

        foreach (var week in await queries.GetWeeklyClaimsAsync(filter, timeZone, ct))
        {
            foreach (var i in week.Items)
            {
                csv.AddRow(
                    week.WeekStart, i.Timestamp, i.PositionId, i.TokenId, i.Protocol, i.Pair, i.Type, i.TxHash,
                    i.Fee0, i.Token0Symbol, i.Fee1, i.Token1Symbol, i.RewardAmount, i.RewardTokenSymbol,
                    i.FeesUsd, i.RewardsUsd, i.TotalUsd, i.HasMissingPrices);
            }
        }
        return csv.ToBytes();
    }

    public async Task<byte[]> DailyEarningsAsync(PositionFilter filter, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var csv = new CsvBuilder("date", "fees_usd", "rewards_usd", "total_usd", "missing_prices");
        foreach (var d in (await queries.GetDailyEarningsAsync(filter, timeZone, ct)).OrderBy(d => d.Date))
        {
            csv.AddRow(d.Date, d.FeesUsd, d.RewardsUsd, d.TotalUsd, d.HasMissingPrices);
        }
        return csv.ToBytes();
    }
}
