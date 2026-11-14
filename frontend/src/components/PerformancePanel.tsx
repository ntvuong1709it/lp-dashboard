import { usePerformance } from "../api/client";
import type { PositionDto, PositionPerformanceDto } from "../api/types";
import { formatPercent, formatUsd } from "../lib/format";
import { poolComparisonText } from "../lib/performance";
import { useHideBalances } from "../lib/privacy";
import { MiniStat, Panel, Skeleton } from "./ui";

/**
 * Rates beyond the lifetime APR: what the position earns now (7 and 30 days), what it earns per day in range, how it
 * compares with the pool's average liquidity, and how long catching up with simply holding would take.
 */
export function PerformancePanel({ position: p }: { position: PositionDto }) {
  useHideBalances();
  const { data, error, isLoading } = usePerformance(p.owner);
  const row: PositionPerformanceDto | undefined = data?.positions.find(
    (r) => r.positionId === p.id,
  );
  const open = p.status === "Open";

  return (
    <Panel title="Performance">
      {error ? (
        <p className="text-sm text-muted">Could not load performance figures.</p>
      ) : isLoading ? (
        <Skeleton className="h-24 w-full" />
      ) : !row ? (
        <p className="text-sm text-muted">No performance figures for this position.</p>
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <MiniStat
              label="APR, lifetime"
              value={formatPercent(p.metrics.feeAprPercent)}
            />
            <MiniStat
              label="APR per day in range"
              value={formatPercent(row.inRangeAprPercent)}
            />
            {open && (
              <>
                <MiniStat
                  label="APR, last 7 days"
                  value={formatPercent(row.apr7d?.totalAprPercent)}
                />
                <MiniStat
                  label="APR, last 30 days"
                  value={formatPercent(row.apr30d?.totalAprPercent)}
                />
              </>
            )}
          </div>

          <ul className="space-y-1.5 text-sm text-fg-2">
            {open && row.vsPool && (
              <li>
                <span className="text-muted">Against the pool: </span>
                {poolComparisonText(row.vsPool)}
                {row.vsPool.incomeKind === "rewards" && (
                  <span className="text-faint"> (staked, so rewards, not swap fees)</span>
                )}
              </li>
            )}
            {open && !row.vsPool && (
              <li className="text-faint">
                No pool comparison: {p.isStaked
                  ? "the data source has no emission rate for this pool, and a staked position earns emissions rather than swap fees."
                  : "the data source has no fee data for this pool."}
              </li>
            )}
            {row.inRangeAprPercent == null && (
              <li className="text-faint">
                APR per day in range needs time in range to cover at least half of the position&apos;s life.
              </li>
            )}
            {open && !row.apr30d && (
              <li className="text-faint">
                {row.apr7d ? "The 30-day rate needs" : "The 7- and 30-day rates need"} a position older than the window.
              </li>
            )}
          </ul>

          <p className="text-xs text-faint">
            Rates are fees plus reward tokens, claimed or not, on the average capital of the window
            {row.apr30d && ` (${formatUsd(row.apr30d.averageCapitalUsd)} over 30 days)`}.
            Rewards are valued at the price when earned, so a falling reward token lowers them.
          </p>
        </div>
      )}
    </Panel>
  );
}
