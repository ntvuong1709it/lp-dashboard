import { Fragment, useMemo } from "react";
import type { PositionDto, PositionEarningsDto } from "../../api/types";
import { usePerformance } from "../../api/client";
import { addDays } from "../../lib/earnings";
import { formatPercent, formatUsd, protocolLabels } from "../../lib/format";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { usePersistentState } from "../../lib/usePersistentState";
import { groupByWallet, useWallets } from "../../lib/wallets";
import { WalletChip, WalletGroupLabel } from "../WalletChip";
import { Panel, Segmented } from "../ui";

type State = "earning" | "idle" | "closed";

const stateChip: Record<State, { label: string; cls: string }> = {
  earning: { label: "Earning", cls: "bg-emerald-500/15 text-emerald-400" },
  idle: { label: "Not earning", cls: "bg-rose-500/15 text-rose-400" },
  closed: { label: "Closed", cls: "bg-slate-700/60 text-slate-400" },
};

/** Which positions produce the earnings, over the last 30 days. */
export function PositionBreakdown({
  rows,
  open,
  today,
  owner,
}: {
  rows: PositionEarningsDto[];
  open: PositionDto[];
  today: string;
  owner: string | null;
}) {
  const drawer = usePositionDrawer();
  const { data: perf } = usePerformance(owner);
  const walletOf = useWallets();
  const [grouping, setGrouping] = usePersistentState<"wallet" | "flat">("lpdash.earnings.group", "wallet");

  const shown = useMemo(
    () => rows.filter((r) => r.status === "Open" || r.last30Usd > 0).sort((a, b) => b.last30Usd - a.last30Usd),
    [rows],
  );
  if (!shown.length) return null;

  const walletCount = new Set(shown.map((r) => (r.owner ?? "").toLowerCase())).size;
  const groups =
    grouping === "wallet" && walletCount > 1
      ? groupByWallet(shown, (r) => r.owner ?? "", walletOf, (r) => r.last30Usd)
      : null;
  const ordered = groups ? groups.flatMap((g) => g.items) : shown;
  const groupStart = new Map(groups?.map((g) => [g.items[0].positionId, g]) ?? []);

  const total30 = shown.reduce((s, r) => s + r.last30Usd, 0);
  const rangeById = new Map(open.map((p) => [p.id, p.range?.inRange]));
  const aprById = new Map((perf?.positions ?? []).map((p) => [p.positionId, p.apr30d ?? p.apr7d]));

  const stateOf = (r: PositionEarningsDto): State =>
    r.status === "Closed"
      ? "closed"
      : r.lastEarnedDate != null && r.lastEarnedDate >= addDays(today, -2)
        ? "earning"
        : "idle";

  return (
    <Panel
      title="Where it comes from · by position, last 30 days"
      actions={
        walletCount > 1 ? (
          <Segmented
            label="Group positions"
            options={[
              { label: "By wallet", value: "wallet" },
              { label: "Flat", value: "flat" },
            ]}
            value={grouping}
            onChange={setGrouping}
          />
        ) : undefined
      }
    >
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-left text-xs uppercase text-slate-500">
            <tr>
              <th className="py-1 pr-3">Position</th>
              <th className="py-1 px-3">Status</th>
              <th className="py-1 px-3 text-right">Yesterday</th>
              <th className="py-1 px-3 text-right">Avg / day (7d)</th>
              <th className="py-1 px-3 text-right">30 days</th>
              <th className="py-1 px-3">Share</th>
              <th className="py-1 pl-3 text-right">APR (30d)</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {ordered.map((r) => {
              const state = stateOf(r);
              const chip = stateChip[state];
              const share = total30 > 0 ? r.last30Usd / total30 : 0;
              const apr = aprById.get(r.positionId);
              const group = groupStart.get(r.positionId);
              const sum = (pick: (e: PositionEarningsDto) => number) =>
                group ? group.items.reduce((s, e) => s + pick(e), 0) : 0;
              return (
                <Fragment key={r.positionId}>
                  {group && (
                    <tr className="bg-slate-950/60 font-medium">
                      <td colSpan={2} className="py-1.5 pr-3">
                        <WalletGroupLabel wallet={group.wallet} count={group.items.length} noun="position" />
                      </td>
                      <td className="px-3 py-1.5 text-right num">{formatUsd(sum((e) => e.yesterdayUsd))}</td>
                      <td className="px-3 py-1.5 text-right num">{formatUsd(sum((e) => e.last7Usd) / 7)}</td>
                      <td className="px-3 py-1.5 text-right num">{formatUsd(sum((e) => e.last30Usd))}</td>
                      <td className="px-3 py-1.5 num">
                        {Math.round((total30 > 0 ? sum((e) => e.last30Usd) / total30 : 0) * 100)}%
                      </td>
                      <td className="py-1.5 pl-3" />
                    </tr>
                  )}
                <tr className={state === "idle" ? "text-slate-400" : ""}>
                  <td className="whitespace-nowrap py-1.5 pr-3">
                    <button
                      type="button"
                      className="text-sky-400 hover:underline"
                      onClick={() => drawer.open(r.positionId)}
                    >
                      {r.pair} #{r.tokenId}
                    </button>
                    {!groups && walletCount > 1 && (
                      <span className="ml-2 align-middle">
                        <WalletChip wallet={walletOf(r.owner ?? "")} />
                      </span>
                    )}
                    <div className="text-xs text-slate-500">
                      {protocolLabels[r.protocol]}
                      {rangeById.get(r.positionId) === false && " · out of range"}
                    </div>
                  </td>
                  <td className="whitespace-nowrap px-3 py-1.5">
                    <span className={`rounded-full px-2 text-[11px] font-semibold uppercase tracking-wide ${chip.cls}`}>
                      {chip.label}
                    </span>
                  </td>
                  <td className="px-3 py-1.5 text-right num">{formatUsd(r.yesterdayUsd)}</td>
                  <td className="px-3 py-1.5 text-right num">{formatUsd(r.last7Usd / 7)}</td>
                  <td className="px-3 py-1.5 text-right num">{formatUsd(r.last30Usd)}</td>
                  <td className="whitespace-nowrap px-3 py-1.5">
                    <span className="num">{Math.round(share * 100)}%</span>
                    <span className="ml-2 inline-block h-1.5 w-16 overflow-hidden rounded bg-slate-800 align-middle">
                      <span className="block h-full bg-sky-400" style={{ width: `${share * 100}%` }} />
                    </span>
                  </td>
                  <td className="py-1.5 pl-3 text-right num">
                    {apr ? formatPercent(apr.totalAprPercent) : "—"}
                  </td>
                </tr>
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
