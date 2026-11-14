import { useMemo } from "react";
import type { PositionDto } from "../api/types";
import { UNCLAIMED_ALERT_USD, pairLabel } from "../lib/attention";
import { formatAmount, formatPrice, formatUsd, protocolLabels } from "../lib/format";
import { usePositionDrawer } from "../lib/positionDrawer";
import { Panel, Skeleton } from "./ui";

/** Fees and rewards share the same two colours everywhere unclaimed money is drawn. */
const FEES_BAR = "bg-accent-fill";
const REWARDS_BAR = "bg-warn-fill";

/** Thin two-segment bar: fees then rewards, filling `fraction` of the track. */
export function SplitBar({
  fees,
  rewards,
  fraction = 1,
  className = "h-2",
}: {
  fees: number;
  rewards: number;
  fraction?: number;
  className?: string;
}) {
  const total = fees + rewards;
  const feesShare = total > 0 ? (fees / total) * 100 : 0;
  return (
    <div className={`overflow-hidden rounded-full bg-raised ${className}`}>
      <div
        className="flex h-full"
        style={{ width: `${Math.max(0, Math.min(1, fraction)) * 100}%` }}
      >
        <div className={FEES_BAR} style={{ width: `${feesShare}%` }} />
        <div className={REWARDS_BAR} style={{ width: `${100 - feesShare}%` }} />
      </div>
    </div>
  );
}

function Legend() {
  return (
    <div className="flex gap-4 text-xs text-muted">
      <span>
        <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${FEES_BAR}`} />
        Fees
      </span>
      <span>
        <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${REWARDS_BAR}`} />
        Rewards
      </span>
    </div>
  );
}

/** Pending reward tokens of one kind: how many you will receive and what one is worth today. */
export interface RewardTokenTotal {
  symbol: string;
  amount: number;
  /** Current USD price of one token; null when unknown. */
  priceUsd: number | null;
}

/** Sums pending rewards per reward token across positions. */
export function rewardTotals(positions: PositionDto[]): RewardTokenTotal[] {
  const map = new Map<string, RewardTokenTotal>();
  for (const p of positions) {
    if (!(p.pendingReward > 0)) continue;
    const symbol = p.rewardTokenSymbol ?? "reward";
    const price = p.metrics.rewardPriceUsd ?? null;
    const entry = map.get(symbol);
    if (entry) {
      entry.amount += p.pendingReward;
      entry.priceUsd ??= price;
    } else {
      map.set(symbol, { symbol, amount: p.pendingReward, priceUsd: price });
    }
  }
  return [...map.values()];
}

/** "1,234 AERO @ $0.52", or without the price when it is unknown. */
function formatRewardToken(r: RewardTokenTotal): string {
  const price = r.priceUsd == null ? "" : ` @ ${formatPrice(r.priceUsd)} USD`;
  return `${formatAmount(r.amount)} ${r.symbol}${price}`;
}

/** "12.3%" — one decimal, two below 1% so small shares don't read as zero. */
function formatPercent(part: number, whole: number): string {
  const pct = (part / whole) * 100;
  return `${pct.toFixed(pct < 1 ? 2 : 1)}%`;
}

/** Reward token amount and price, drawn in the rewards colour so the AERO value stands out. */
function RewardTokenLine({
  r,
  valueUsd,
  large = false,
}: {
  r: RewardTokenTotal;
  /** USD value of the whole amount; falls back to amount × price. */
  valueUsd?: number;
  large?: boolean;
}) {
  const value = valueUsd ?? (r.priceUsd == null ? null : r.amount * r.priceUsd);
  return (
    <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 num">
      <span className={`font-semibold text-warn ${large ? "text-lg" : "text-sm"}`}>
        {formatAmount(r.amount)} {r.symbol}
      </span>
      {value != null && (
        <span className={`font-semibold text-warn ${large ? "text-base" : "text-xs"}`}>
          ≈ {formatUsd(value)}
        </span>
      )}
      <span className="text-xs text-muted">
        {r.priceUsd == null ? "price unknown" : `@ ${formatPrice(r.priceUsd)} USD each`}
      </span>
    </div>
  );
}

export function UnclaimedCard({
  fees,
  rewards,
  rewardTokens,
  insights,
  note,
  loading,
  className = "",
}: {
  fees: number;
  rewards: number;
  rewardTokens?: RewardTokenTotal[];
  /** Decision-helping lines (earning rate, claim cost, ...). */
  insights?: string[];
  note?: string;
  loading?: boolean;
  className?: string;
}) {
  const total = fees + rewards;

  return (
    <div
      className={`min-w-0 rounded-lg border border-accent-fill/50 bg-accent-fill/5 p-4 ring-4 ring-accent/10 ${className}`}
    >
      <div className="text-xs font-semibold uppercase tracking-[.05em] text-accent">
        Unclaimed
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-8 w-28" />
      ) : (
        <>
          <div className="mt-1 text-3xl font-semibold tracking-tight text-accent num">
            {formatUsd(total)}
          </div>
          <div className="mt-3">
            <SplitBar fees={fees} rewards={rewards} />
          </div>
          <div className="mt-2 flex justify-between gap-2 text-xs text-fg-2 num">
            <span>
              <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${FEES_BAR}`} />
              Fees {formatUsd(fees)}
            </span>
            <span>
              <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${REWARDS_BAR}`} />
              Rewards {formatUsd(rewards)}
            </span>
          </div>
          {rewardTokens?.map((r) => (
            <div key={r.symbol} className="mt-1 text-xs text-muted num">
              Rewards: {formatRewardToken(r)}
              {r.priceUsd == null && " (price unknown)"}
            </div>
          ))}
          {insights?.map((line) => (
            <div key={line} className="mt-1 text-xs text-fg-2">
              {line}
            </div>
          ))}
          {note && <div className="mt-2 text-xs text-muted">{note}</div>}
        </>
      )}
    </div>
  );
}

export function ReadyToClaimPanel({
  open,
  loading,
}: {
  open: PositionDto[];
  loading: boolean;
}) {
  const drawer = usePositionDrawer();
  const rows = useMemo(
    () =>
      open
        .map((p) => ({
          p,
          fees: p.metrics.uncollectedFeesUsd,
          rewards: p.metrics.pendingRewardsUsd,
          total: p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd,
        }))
        .filter((r) => r.total > 0)
        .sort((a, b) => b.total - a.total),
    [open],
  );
  const max = rows[0]?.total ?? 0;
  const sum = rows.reduce((s, r) => s + r.total, 0);
  const positionValue = rows.reduce((s, r) => s + r.p.metrics.currentValueUsd, 0);
  const tokens = useMemo(() => rewardTotals(rows.map((r) => r.p)), [rows]);
  const rewardsSum = rows.reduce((s, r) => s + r.rewards, 0);

  return (
    <Panel
      title="Ready to claim"
      actions={
        <span className="text-sm text-muted">
          Total{" "}
          <span className="font-semibold text-accent num">{formatUsd(sum)}</span>
          {positionValue > 0 && (
            <span className="num">
              {" · "}
              <span className="font-semibold text-fg-2">
                {formatPercent(sum, positionValue)}
              </span>{" "}
              of position value
            </span>
          )}
        </span>
      }
    >
      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : !rows.length ? (
        <p className="text-sm text-muted">Nothing to claim right now.</p>
      ) : (
        <>
          {tokens.map((t) => (
            <div
              key={t.symbol}
              className="mb-3 rounded-lg border border-warn-fill/40 bg-warn/10 px-3 py-2"
            >
              <RewardTokenLine
                r={t}
                valueUsd={tokens.length === 1 ? rewardsSum : undefined}
                large
              />
            </div>
          ))}
          <Legend />
          <ul className="mt-2 max-h-96 overflow-y-auto">
            {rows.map(({ p, fees, rewards, total }) => (
              <li key={p.id} className="border-t border-line first:border-t-0">
                <button
                  type="button"
                  onClick={() => drawer.open(p.id)}
                  className="block w-full rounded px-2 py-3 text-left hover:bg-raised/50"
                >
                  <div className="flex items-center justify-between gap-3">
                    <div className="min-w-0 truncate">
                      <span className="text-sm font-semibold">
                        {pairLabel(p)} #{p.tokenId}
                      </span>
                      <span className="ml-2 text-xs text-muted">
                        {protocolLabels[p.protocol]} · {p.chain}
                      </span>
                    </div>
                    <div className="flex flex-none items-center gap-2.5">
                      {total >= UNCLAIMED_ALERT_USD && (
                        <span className="rounded-full bg-accent-fill px-2 py-0.5 text-[11px] font-semibold text-surface">
                          Claim now
                        </span>
                      )}
                      <span
                        className={`text-[15px] font-semibold num ${total >= UNCLAIMED_ALERT_USD ? "text-accent" : "text-fg-2"}`}
                      >
                        {formatUsd(total)}
                      </span>
                    </div>
                  </div>
                  <div className="mt-2">
                    <SplitBar
                      fees={fees}
                      rewards={rewards}
                      fraction={max > 0 ? total / max : 0}
                    />
                  </div>
                  <div className="mt-2 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs num">
                    <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                      <span className="text-fg-2">
                        <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${FEES_BAR}`} />
                        Fees <span className="font-semibold">{formatUsd(fees)}</span>
                      </span>
                      <span className="text-fg-2">
                        <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${REWARDS_BAR}`} />
                        Rewards <span className="font-semibold">{formatUsd(rewards)}</span>
                      </span>
                    </div>
                    {p.metrics.currentValueUsd > 0 && (
                      <span className="text-muted">
                        <span className="font-semibold text-fg-2">
                          {formatPercent(total, p.metrics.currentValueUsd)}
                        </span>{" "}
                        of position value
                      </span>
                    )}
                  </div>
                  {p.pendingReward > 0 && (
                    <div className="mt-1.5">
                      <RewardTokenLine
                        r={{
                          symbol: p.rewardTokenSymbol ?? "reward",
                          amount: p.pendingReward,
                          priceUsd: p.metrics.rewardPriceUsd ?? null,
                        }}
                        valueUsd={rewards}
                      />
                    </div>
                  )}
                </button>
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-faint">
            "Claim now" appears above {formatUsd(UNCLAIMED_ALERT_USD)}. Click a
            row to open the position.
          </p>
        </>
      )}
    </Panel>
  );
}
