import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { usePositions, useSummary } from "../api/client";
import type {
  PortfolioSummaryDto,
  PositionDto,
  PositionStatus,
} from "../api/types";
import { RangeBadge, RangeBar } from "../components/RangeBar";
import { Empty, ErrorText, StatCard } from "../components/ui";
import {
  formatAge,
  formatDate,
  formatFee,
  formatPercent,
  formatUsd,
  invertRange,
  pnlClass,
  protocolLabels,
} from "../lib/format";
import { useOwner } from "../lib/owner";

const statusTabs: {
  label: string;
  value: PositionStatus | null;
  count: (s: PortfolioSummaryDto) => number;
}[] = [
  { label: "Open", value: "Open", count: (s) => s.openCount },
  { label: "Closed", value: "Closed", count: (s) => s.closedCount },
  { label: "All", value: null, count: (s) => s.openCount + s.closedCount },
];

export function PortfolioPage() {
  const { owner } = useOwner();
  const [status, setStatus] = useState<PositionStatus | null>("Open");
  const [inverted, setInverted] = useState(false);
  const summary = useSummary(owner, status);
  // Unfiltered totals so tab counts don't depend on the selected tab.
  const totals = useSummary(owner, null).data;
  const positions = usePositions(owner, status);
  const s = summary.data;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
        <StatCard
          label="Value"
          value={formatUsd(s?.currentValueUsd)}
          hint={
            totals && `${totals.openCount} open · ${totals.closedCount} closed`
          }
        />
        <StatCard
          label="Fees earned"
          value={formatUsd(s?.feesEarnedUsd)}
          hint={s && `${formatUsd(s.uncollectedFeesUsd)} unclaimed`}
        />
        <StatCard
          label="P/L"
          value={formatUsd(s?.pnlUsd, true)}
          valueClass={pnlClass(s?.pnlUsd)}
          hint={s && `deposited ${formatUsd(s.depositedUsd)}`}
        />
        <StatCard
          label="P/L vs HODL"
          value={formatUsd(s?.pnlVsHodlUsd, true)}
          valueClass={pnlClass(s?.pnlVsHodlUsd)}
        />
        <StatCard
          label="Impermanent loss"
          value={formatUsd(s?.impermanentLossUsd, true)}
          valueClass={pnlClass(s?.impermanentLossUsd)}
          hint="excl. fees"
        />
        <StatCard
          label="Gas"
          value={formatUsd(s?.gasUsd)}
          hint={s?.hasMissingPrices ? "some prices missing" : undefined}
        />
      </div>

      <div className="flex items-center justify-between">
        <div className="flex gap-1 rounded-md border border-slate-800 p-1">
          {statusTabs.map((t) => (
            <button
              key={t.label}
              onClick={() => setStatus(t.value)}
              className={`flex items-center gap-1.5 rounded px-3 py-1 text-sm ${status === t.value ? "bg-slate-700 text-white" : "text-slate-400 hover:text-white"}`}
            >
              {t.label}
              {totals && (
                <span
                  className={`num rounded-full px-1.5 text-xs ${status === t.value ? "bg-slate-600 text-slate-100" : "bg-slate-800 text-slate-400"}`}
                >
                  {t.count(totals)}
                </span>
              )}
            </button>
          ))}
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-400">
          <input
            type="checkbox"
            checked={inverted}
            onChange={(e) => setInverted(e.target.checked)}
          />
          Invert prices
        </label>
      </div>

      {positions.error ? (
        <ErrorText error={positions.error} />
      ) : positions.isLoading ? (
        <Empty>Loading…</Empty>
      ) : !positions.data?.length ? (
        totals && totals.openCount + totals.closedCount > 0 ? (
          <Empty>No {status?.toLowerCase()} positions.</Empty>
        ) : (
          <Empty>
            No positions yet.{" "}
            <Link className="text-sky-400 underline" to="/addresses">
              Add a wallet
            </Link>{" "}
            and wait for the first sync.
          </Empty>
        )
      ) : (
        <PositionsTable positions={positions.data} inverted={inverted} />
      )}
    </div>
  );
}

function PositionsTable({
  positions,
  inverted,
}: {
  positions: PositionDto[];
  inverted: boolean;
}) {
  const navigate = useNavigate();

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800">
      <table className="w-full text-sm">
        <thead className="bg-slate-900 text-left text-xs uppercase text-slate-400">
          <tr>
            <th className="px-3 py-2 w-10 text-right">#</th>
            <th className="px-3 py-2">Position</th>
            <th className="px-3 py-2 w-64">Range</th>
            <th className="px-3 py-2">Age</th>
            <th className="px-3 py-2">Created</th>
            <th className="px-3 py-2">Withdrawn</th>
            <th className="px-3 py-2 text-right">Value</th>
            <th className="px-3 py-2 text-right">Fees</th>
            <th className="px-3 py-2 text-right">P/L</th>
            <th className="px-3 py-2 text-right">IL</th>
            <th className="px-3 py-2 text-right">vs HODL</th>
            <th className="px-3 py-2 text-right">Fee APR</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800">
          {positions.map((p, index) => {
            const m = p.metrics;
            const range =
              p.range && (inverted ? invertRange(p.range) : p.range);
            const [base, quote] = inverted
              ? [p.token1, p.token0]
              : [p.token0, p.token1];
            return (
              <tr
                key={p.id}
                className="cursor-pointer hover:bg-slate-900/70"
                onClick={() => navigate(`/positions/${p.id}`)}
              >
                <td className="px-3 py-2 text-right num text-slate-500">
                  {index + 1}
                </td>
                <td className="px-3 py-2">
                  <div className="font-medium">
                    {p.token0?.symbol ?? "?"}/{p.token1?.symbol ?? "?"}{" "}
                    <span className="text-xs text-slate-500">
                      {formatFee(p.fee)}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 text-xs text-slate-500">
                    <span>{protocolLabels[p.protocol]}</span>
                    <span>#{p.tokenId}</span>
                    {p.isStaked && <span className="text-sky-400">staked</span>}
                  </div>
                </td>
                <td className="px-3 py-2">
                  {range ? (
                    <div className="space-y-1">
                      <RangeBar range={range} showLabels />
                      <div className="flex items-center justify-between text-[11px] text-slate-500">
                        <span>
                          {quote?.symbol} per {base?.symbol}
                        </span>
                        <RangeBadge
                          inRange={range.inRange}
                          closed={p.status === "Closed"}
                        />
                      </div>
                    </div>
                  ) : (
                    "—"
                  )}
                </td>
                <td className="px-3 py-2 num">{formatAge(m.ageDays)}</td>
                <td className="px-3 py-2 num whitespace-nowrap text-slate-400">
                  {formatDate(p.openedAt)}
                </td>
                <td className="px-3 py-2 num whitespace-nowrap text-slate-400">
                  {formatDate(p.closedAt)}
                </td>
                <td className="px-3 py-2 text-right num">
                  {formatUsd(m.currentValueUsd)}
                </td>
                <td className="px-3 py-2 text-right num">
                  <div>{formatUsd(m.feesEarnedUsd)}</div>
                  <div className="text-xs text-slate-500">
                    {formatUsd(m.uncollectedFeesUsd + m.pendingRewardsUsd)}{" "}
                    unclaimed
                  </div>
                </td>
                <td
                  className={`px-3 py-2 text-right num ${pnlClass(m.pnlUsd)}`}
                >
                  <div>{formatUsd(m.pnlUsd, true)}</div>
                  <div className="text-xs opacity-80">
                    {formatPercent(m.pnlPercent, true)}
                  </div>
                </td>
                <td
                  className={`px-3 py-2 text-right num ${pnlClass(m.impermanentLossUsd)}`}
                >
                  {formatUsd(m.impermanentLossUsd, true)}
                </td>
                <td
                  className={`px-3 py-2 text-right num ${pnlClass(m.pnlVsHodlUsd)}`}
                >
                  {formatUsd(m.pnlVsHodlUsd, true)}
                </td>
                <td className="px-3 py-2 text-right num">
                  {formatPercent(m.feeAprPercent)}
                  {m.hasMissingPrices && (
                    <div
                      className="text-[11px] text-amber-400"
                      title="Some historical prices are missing"
                    >
                      prices missing
                    </div>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
