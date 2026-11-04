import { useMemo } from "react";
import type { PositionDto } from "../api/types";
import { UNCLAIMED_ALERT_USD, pairLabel } from "../lib/attention";
import { formatUsd, protocolLabels } from "../lib/format";
import { usePositionDrawer } from "../lib/positionDrawer";
import { Panel, Skeleton } from "./ui";

/** Fees and rewards share the same two colours everywhere unclaimed money is drawn. */
const FEES_BAR = "bg-sky-400";
const REWARDS_BAR = "bg-amber-400";

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
    <div className={`overflow-hidden rounded-full bg-slate-800 ${className}`}>
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
    <div className="flex gap-4 text-xs text-slate-400">
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

export function UnclaimedCard({
  fees,
  rewards,
  note,
  loading,
  className = "",
}: {
  fees: number;
  rewards: number;
  note?: string;
  loading?: boolean;
  className?: string;
}) {
  const total = fees + rewards;

  return (
    <div
      className={`min-w-0 rounded-lg border border-sky-700 bg-sky-950/40 p-4 ring-4 ring-sky-400/10 ${className}`}
    >
      <div className="text-xs font-semibold uppercase tracking-[.05em] text-sky-300">
        Unclaimed
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-8 w-28" />
      ) : (
        <>
          <div className="mt-1 text-3xl font-semibold tracking-tight text-sky-400 num">
            {formatUsd(total)}
          </div>
          <div className="mt-3">
            <SplitBar fees={fees} rewards={rewards} />
          </div>
          <div className="mt-2 flex justify-between gap-2 text-xs text-slate-300 num">
            <span>
              <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${FEES_BAR}`} />
              Fees {formatUsd(fees)}
            </span>
            <span>
              <span className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${REWARDS_BAR}`} />
              Rewards {formatUsd(rewards)}
            </span>
          </div>
          {note && <div className="mt-2 text-xs text-slate-400">{note}</div>}
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

  return (
    <Panel
      title="Ready to claim"
      actions={
        <span className="text-sm text-slate-400">
          Total{" "}
          <span className="font-semibold text-sky-400 num">{formatUsd(sum)}</span>
        </span>
      }
    >
      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : !rows.length ? (
        <p className="text-sm text-slate-400">Nothing to claim right now.</p>
      ) : (
        <>
          <Legend />
          <ul className="mt-2 max-h-96 overflow-y-auto">
            {rows.map(({ p, fees, rewards, total }) => (
              <li key={p.id} className="border-t border-slate-800 first:border-t-0">
                <button
                  type="button"
                  onClick={() => drawer.open(p.id)}
                  className="block w-full rounded px-2 py-3 text-left hover:bg-slate-800/50"
                >
                  <div className="flex items-center justify-between gap-3">
                    <div className="min-w-0 truncate">
                      <span className="text-sm font-semibold">
                        {pairLabel(p)} #{p.tokenId}
                      </span>
                      <span className="ml-2 text-xs text-slate-400">
                        {protocolLabels[p.protocol]} · {p.chain}
                      </span>
                    </div>
                    <div className="flex flex-none items-center gap-2.5">
                      {total >= UNCLAIMED_ALERT_USD && (
                        <span className="rounded-full bg-sky-400 px-2 py-0.5 text-[11px] font-semibold text-sky-950">
                          Claim now
                        </span>
                      )}
                      <span
                        className={`text-[15px] font-semibold num ${total >= UNCLAIMED_ALERT_USD ? "text-sky-400" : "text-slate-200"}`}
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
                  <div className="mt-1.5 text-xs text-slate-400 num">
                    fees {formatUsd(fees)} · rewards {formatUsd(rewards)}
                    {p.metrics.currentValueUsd > 0 &&
                      ` · ${((total / p.metrics.currentValueUsd) * 100).toFixed(1)}% of position value`}
                  </div>
                </button>
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-500">
            "Claim now" appears above {formatUsd(UNCLAIMED_ALERT_USD)}. Click a
            row to open the position.
          </p>
        </>
      )}
    </Panel>
  );
}
