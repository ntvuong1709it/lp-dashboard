import type { PositionDto, PositionEventDto } from "../api/types";
import { formatUsd } from "./format";

const DAY_MS = 86_400_000;

/** Shorter than this, a per-day rate is mostly noise. */
const MIN_DAYS_FOR_RATE = 0.5;

/** Past claim transactions: when the last one happened and what one typically costs in gas. */
export function claimHistory(events: PositionEventDto[]) {
  const byTx = new Map<string, { time: number; gasUsd: number }>();
  for (const e of events) {
    if (e.type !== "Collect" && e.type !== "RewardClaim") continue;
    // A staked claim can emit several events in one transaction; the gas belongs to the transaction.
    const entry = byTx.get(e.txHash) ?? { time: 0, gasUsd: 0 };
    entry.time = Math.max(entry.time, new Date(e.timestamp).getTime());
    entry.gasUsd = Math.max(entry.gasUsd, e.gasUsd ?? 0);
    byTx.set(e.txHash, entry);
  }
  const claims = [...byTx.values()];
  const gas = claims.map((c) => c.gasUsd).filter((g) => g > 0).sort((a, b) => a - b);
  return {
    lastClaimAt: claims.length ? Math.max(...claims.map((c) => c.time)) : null,
    /** Median gas of past claims; null when there is no priced claim to go by. */
    typicalGasUsd: gas.length ? gas[Math.floor(gas.length / 2)] : null,
  };
}

function ago(days: number): string {
  if (days < 1) return `${Math.max(1, Math.round(days * 24))}h`;
  return `${Math.floor(days)}d`;
}

/**
 * Lines that help decide whether to claim now: how fast the unclaimed money grows, how long it has been waiting,
 * what claiming costs, and how much of it is a volatile reward token.
 */
export function claimInsight(
  p: PositionDto,
  events: PositionEventDto[],
  now = Date.now(),
): string[] {
  const unclaimed = p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd;
  if (unclaimed <= 0) return [];

  const { lastClaimAt, typicalGasUsd } = claimHistory(events);
  const since = lastClaimAt ?? (p.openedAt ? new Date(p.openedAt).getTime() : null);
  const lines: string[] = [];

  if (since != null) {
    const days = (now - since) / DAY_MS;
    const when = lastClaimAt != null ? `Last claimed ${ago(days)} ago` : `Never claimed, opened ${ago(days)} ago`;
    if (days >= MIN_DAYS_FOR_RATE) {
      const perDay = unclaimed / days;
      lines.push(`Earning ~${formatUsd(perDay)}/day (~${formatUsd(perDay * 7)}/week, ~${formatUsd(perDay * 30)}/month) · ${when}`);
    } else {
      lines.push(when);
    }
  }

  if (typicalGasUsd != null) {
    const pct = (typicalGasUsd / unclaimed) * 100;
    lines.push(
      `Claiming costs ~${formatUsd(typicalGasUsd)} gas (typical for this position), ${pct < 0.1 ? "<0.1" : pct.toFixed(1)}% of what you would receive`,
    );
  }

  const rewardShare = p.metrics.pendingRewardsUsd / unclaimed;
  if (rewardShare >= 0.5 && p.rewardTokenSymbol) {
    lines.push(
      `${(rewardShare * 100).toFixed(0)}% of unclaimed is ${p.rewardTokenSymbol}: a 10% price drop costs ${formatUsd(p.metrics.pendingRewardsUsd * 0.1)}`,
    );
  }

  return lines;
}
