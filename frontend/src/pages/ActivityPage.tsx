import { useMemo } from "react";
import { useActivity } from "../api/client";
import type { PositionEventType } from "../api/types";
import { EarningsTabs } from "../components/EarningsTabs";
import { UpdatedAgo } from "../components/UpdatedAgo";
import {
  CopyButton,
  Empty,
  ErrorText,
  ExternalLink,
  Segmented,
  TableSkeleton,
} from "../components/ui";
import { formatAmount, formatUsd, protocolLabels, txUrl } from "../lib/format";
import { useOwner } from "../lib/owner";
import { usePositionDrawer } from "../lib/positionDrawer";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";

const typeLabels: Record<PositionEventType, string> = {
  IncreaseLiquidity: "Add liquidity",
  DecreaseLiquidity: "Remove liquidity",
  Collect: "Collect fees",
  RewardClaim: "Claim rewards",
};

type Filter = PositionEventType | null;
const filters: { label: string; value: Filter }[] = [
  { label: "All", value: null },
  { label: "Add", value: "IncreaseLiquidity" },
  { label: "Remove", value: "DecreaseLiquidity" },
  { label: "Collect", value: "Collect" },
  { label: "Rewards", value: "RewardClaim" },
];

const dateTime = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

export function ActivityPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const drawer = usePositionDrawer();
  const [filter, setFilter] = usePersistentState<Filter>(
    "lpdash.activity.filter",
    null,
  );
  const { data, error, isLoading } = useActivity(owner);

  const rows = useMemo(
    () => (data ?? []).filter((a) => !filter || a.event.type === filter),
    [data, filter],
  );

  return (
    <div className="space-y-6">
      <EarningsTabs />
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Activity</h1>
          <p className="text-sm text-slate-400">
            Adds, removals and fee collections across all positions.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          <UpdatedAgo />
          <Segmented
            label="Event type"
            options={filters}
            value={filter}
            onChange={setFilter}
          />
        </div>
      </div>

      {error ? (
        <ErrorText error={error} />
      ) : isLoading ? (
        <TableSkeleton />
      ) : !rows.length ? (
        <Empty>
          {data?.length ? "No events of this type." : "No activity yet."}
        </Empty>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
          <table className="w-full min-w-[44rem] text-sm">
            <thead className="text-left text-xs uppercase text-slate-400">
              <tr>
                <th className="px-3 py-2">Date</th>
                <th className="px-3 py-2">Event</th>
                <th className="px-3 py-2">Position</th>
                <th className="px-3 py-2 text-right">Value</th>
                <th className="px-3 py-2">Transaction</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800">
              {rows.map((a) => {
                const e = a.event;
                return (
                  <tr key={`${a.positionId}-${e.id}`} className="hover:bg-slate-800/40">
                    <td className="whitespace-nowrap px-3 py-2.5 text-slate-400 num">
                      {dateTime.format(new Date(e.timestamp))}
                    </td>
                    <td className="px-3 py-2.5">
                      <span className="rounded bg-slate-800 px-1.5 py-0.5 text-xs">
                        {typeLabels[e.type]}
                      </span>
                      {e.type === "RewardClaim" && (
                        <span className="ml-2 text-xs text-slate-500 num">
                          {formatAmount(e.rewardAmount)} {e.rewardTokenSymbol}
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2.5">
                      <button
                        type="button"
                        className="text-left hover:underline"
                        onClick={() => drawer.open(a.positionId)}
                      >
                        {a.pair}{" "}
                        <span className="text-slate-500">#{a.tokenId}</span>
                      </button>
                      <span className="ml-2 text-xs text-slate-500">
                        {protocolLabels[a.protocol]} · {a.chain}
                      </span>
                    </td>
                    <td className="px-3 py-2.5 text-right num">
                      {formatUsd(e.valueUsd)}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2.5 text-xs text-slate-500 num">
                      <ExternalLink
                        href={txUrl(a.chain, e.txHash)}
                        title="Open the transaction in the block explorer"
                      >
                        {e.txHash.slice(0, 10)}…
                      </ExternalLink>
                      <CopyButton text={e.txHash} title="Copy transaction hash" />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {(data?.length ?? 0) >= 500 && (
            <p className="border-t border-slate-800 px-3 py-2 text-xs text-slate-500">
              Showing the most recent 500 events.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
