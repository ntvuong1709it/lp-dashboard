import { useMemo } from "react";
import { Link } from "react-router-dom";
import { usePositionDrawer } from "../lib/positionDrawer";
import { exportUrl, usePositions, useSummary } from "../api/client";
import type {
  PortfolioSummaryDto,
  PositionDto,
  PositionStatus,
  Protocol,
} from "../api/types";
import { RangeBadge, RangeBar } from "../components/RangeBar";
import { ExportMenu } from "../components/ExportMenu";
import { UpdatedAgo } from "../components/UpdatedAgo";
import {
  CopyButton,
  Empty,
  ErrorText,
  ExternalLink,
  TableSkeleton,
} from "../components/ui";
import {
  estimateMark,
  formatAge,
  formatDate,
  formatFee,
  formatHours,
  formatPercent,
  formatShare,
  formatUsd,
  invertRange,
  pnlClass,
  poolLinks,
  protocolLabels,
  timeInRangeSource,
} from "../lib/format";
import { isNearEdge } from "../lib/attention";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import {
  filterPositions,
  sortPositions,
  type SortKey,
  type SortState,
} from "../lib/positionTable";
import { usePersistentState } from "../lib/usePersistentState";

const statusTabs: {
  label: string;
  value: PositionStatus | null;
  count: (s: PortfolioSummaryDto) => number;
}[] = [
  { label: "Open", value: "Open", count: (s) => s.openCount },
  { label: "Closed", value: "Closed", count: (s) => s.closedCount },
  { label: "All", value: null, count: (s) => s.openCount + s.closedCount },
];

export function PositionsPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const [status, setStatus] = usePersistentState<PositionStatus | null>(
    "lpdash.portfolio.status",
    "Open",
  );
  const [inverted, setInverted] = usePersistentState(
    "lpdash.portfolio.inverted",
    false,
  );
  const [search, setSearch] = usePersistentState("lpdash.portfolio.search", "");
  const [protocol, setProtocol] = usePersistentState<Protocol | "">(
    "lpdash.portfolio.protocol",
    "",
  );
  const [inRangeOnly, setInRangeOnly] = usePersistentState(
    "lpdash.portfolio.inRangeOnly",
    false,
  );
  const [sort, setSort] = usePersistentState<SortState>(
    "lpdash.portfolio.sort",
    { key: "created", dir: "desc" },
  );
  // Unfiltered totals so tab counts don't depend on the selected tab.
  const totals = useSummary(owner, null).data;
  const positions = usePositions(owner, status);

  const protocols = useMemo(
    () => [...new Set((positions.data ?? []).map((p) => p.protocol))],
    [positions.data],
  );
  const visible = useMemo(
    () =>
      sortPositions(
        filterPositions(positions.data ?? [], search, protocol, inRangeOnly),
        sort,
      ),
    [positions.data, search, protocol, inRangeOnly, sort],
  );
  const filtered = !!search.trim() || !!protocol || inRangeOnly;
  const toggleSort = (key: SortKey) =>
    setSort(
      sort.key === key
        ? { key, dir: sort.dir === "asc" ? "desc" : "asc" }
        : { key, dir: key === "position" ? "asc" : "desc" },
    );

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
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
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          <UpdatedAgo />
          <ExportMenu
            items={[
              {
                label: `Positions (${status?.toLowerCase() ?? "all"})`,
                href: exportUrl("positions", { owner, status }),
              },
              {
                label: "All events (transactions)",
                href: exportUrl("events", { owner }),
              },
            ]}
          />
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search token, pool, #id…"
            className="w-52 rounded-md border border-slate-700 bg-slate-900 px-2 py-1.5 text-slate-200 placeholder:text-slate-500"
          />
          {(protocols.length > 1 || protocol !== "") && (
            <select
              value={protocol}
              onChange={(e) => setProtocol(e.target.value as Protocol | "")}
              className="rounded-md border border-slate-700 bg-slate-900 px-2 py-1.5 text-slate-200"
            >
              <option value="">All protocols</option>
              {protocols.map((p) => (
                <option key={p} value={p}>
                  {protocolLabels[p]}
                </option>
              ))}
            </select>
          )}
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={inRangeOnly}
              onChange={(e) => setInRangeOnly(e.target.checked)}
            />
            In range only
          </label>
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={inverted}
              onChange={(e) => setInverted(e.target.checked)}
            />
            Invert prices
          </label>
        </div>
      </div>

      {positions.error ? (
        <ErrorText error={positions.error} />
      ) : positions.isLoading ? (
        <TableSkeleton />
      ) : !visible.length ? (
        filtered && positions.data?.length ? (
          <Empty>No positions match the filters.</Empty>
        ) : totals && totals.openCount + totals.closedCount > 0 ? (
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
        <PositionsTable
          positions={visible}
          inverted={inverted}
          sort={sort}
          onSort={toggleSort}
        />
      )}
    </div>
  );
}

function SortHeader({
  label,
  k,
  sort,
  onSort,
  right,
}: {
  label: string;
  k: SortKey;
  sort: SortState;
  onSort: (key: SortKey) => void;
  right?: boolean;
}) {
  const active = sort.key === k;
  return (
    <th
      className={`px-3 py-2 ${right ? "text-right" : ""}`}
      aria-sort={
        active ? (sort.dir === "asc" ? "ascending" : "descending") : "none"
      }
    >
      <button
        type="button"
        onClick={() => onSort(k)}
        className={`uppercase hover:text-white ${active ? "text-white" : ""}`}
      >
        {label}
        <span className="ml-1 inline-block w-2">
          {active ? (sort.dir === "asc" ? "▲" : "▼") : ""}
        </span>
      </button>
    </th>
  );
}

function PositionsTable({
  positions,
  inverted,
  sort,
  onSort,
}: {
  positions: PositionDto[];
  inverted: boolean;
  sort: SortState;
  onSort: (key: SortKey) => void;
}) {
  const drawer = usePositionDrawer();
  const sum = (pick: (m: PositionDto["metrics"]) => number) =>
    positions.reduce((total, p) => total + pick(p.metrics), 0);

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800">
      <table className="w-full text-sm">
        <thead className="bg-slate-900 text-left text-xs uppercase text-slate-400">
          <tr>
            <th className="px-3 py-2 w-10 text-right">#</th>
            <SortHeader label="Position" k="position" sort={sort} onSort={onSort} />
            <th className="px-3 py-2 w-64">Range</th>
            <SortHeader label="Age" k="age" sort={sort} onSort={onSort} />
            <SortHeader label="Created" k="created" sort={sort} onSort={onSort} />
            <SortHeader label="Withdrawn" k="closed" sort={sort} onSort={onSort} />
            <SortHeader label="Value" k="value" right sort={sort} onSort={onSort} />
            <SortHeader label="Fees" k="fees" right sort={sort} onSort={onSort} />
            <SortHeader label="P/L" k="pnl" right sort={sort} onSort={onSort} />
            <SortHeader label="IL" k="il" right sort={sort} onSort={onSort} />
            <SortHeader label="vs HODL" k="vsHodl" right sort={sort} onSort={onSort} />
            <SortHeader label="Fee APR" k="apr" right sort={sort} onSort={onSort} />
            <SortHeader label="In range" k="inRange" right sort={sort} onSort={onSort} />
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
                onClick={() => drawer.open(p.id)}
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
                    <CopyButton text={p.tokenId} title="Copy token ID" />
                    {p.isStaked && <span className="text-sky-400">staked</span>}
                    {p.poolAddress &&
                      poolLinks(p.chain, p.protocol, p.poolAddress).map((l) => (
                        <ExternalLink
                          key={l.label}
                          href={l.url}
                          title={`Open the pool on ${l.label}`}
                        >
                          {l.label}
                        </ExternalLink>
                      ))}
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
                          nearEdge={isNearEdge(range)}
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
                      title="Some historical prices are unavailable and counted as $0, so these figures may be understated"
                    >
                      prices missing
                    </div>
                  )}
                  {!m.hasMissingPrices && m.hasEstimatedPrices && (
                    <div
                      className="text-[11px] text-amber-400/80"
                      title="Some reward claims had no historical price; the nearest later known price was used"
                    >
                      est. reward price
                    </div>
                  )}
                </td>
                <td className="px-3 py-2 text-right num">
                  {p.timeInRange ? (
                    <>
                      <div>
                        {estimateMark(p.timeInRange)}
                        {formatShare(p.timeInRange.percent)}
                      </div>
                      <div
                        className="text-xs text-slate-500"
                        title={timeInRangeSource(p.timeInRange)}
                      >
                        of {formatHours(p.timeInRange.trackedHours)}
                      </div>
                    </>
                  ) : (
                    <span
                      className="text-slate-500"
                      title="No snapshots and no price history available for this position"
                    >
                      n/a
                    </span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot className="border-t border-slate-700 bg-slate-900 font-medium">
          <tr>
            <td colSpan={6} className="px-3 py-2 text-slate-400">
              Total · {positions.length}{" "}
              {positions.length === 1 ? "position" : "positions"}
            </td>
            <td className="px-3 py-2 text-right num">
              {formatUsd(sum((m) => m.currentValueUsd))}
            </td>
            <td className="px-3 py-2 text-right num">
              <div>{formatUsd(sum((m) => m.feesEarnedUsd))}</div>
              <div className="text-xs font-normal text-slate-500">
                {formatUsd(
                  sum((m) => m.uncollectedFeesUsd + m.pendingRewardsUsd),
                )}{" "}
                unclaimed
              </div>
            </td>
            <td
              className={`px-3 py-2 text-right num ${pnlClass(sum((m) => m.pnlUsd))}`}
            >
              {formatUsd(sum((m) => m.pnlUsd), true)}
            </td>
            <td
              className={`px-3 py-2 text-right num ${pnlClass(sum((m) => m.impermanentLossUsd))}`}
            >
              {formatUsd(sum((m) => m.impermanentLossUsd), true)}
            </td>
            <td
              className={`px-3 py-2 text-right num ${pnlClass(sum((m) => m.pnlVsHodlUsd))}`}
            >
              {formatUsd(sum((m) => m.pnlVsHodlUsd), true)}
            </td>
            <td colSpan={2} className="px-3 py-2" />
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
