import { useMemo } from "react";
import { Link } from "react-router-dom";
import { usePositionDrawer } from "../lib/positionDrawer";
import { usePerformance, usePositions, useSummary } from "../api/client";
import type {
  HodlBreakEvenDto,
  PositionDto,
  Protocol,
} from "../api/types";
import { RangeBadge, RangeBar } from "../components/RangeBar";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { SplitBar } from "../components/Unclaimed";
import {
  CopyButton,
  Empty,
  ErrorText,
  ExternalLink,
  Segmented,
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
  timeInRangeBand,
  timeInRangeSource,
} from "../lib/format";
import { isNearEdge } from "../lib/attention";
import { catchUpCell } from "../lib/performance";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import {
  filterPositions,
  sortPositions,
  type SortKey,
  type SortState,
} from "../lib/positionTable";
import { usePersistentState } from "../lib/usePersistentState";

type StatusFilter = "all" | "open" | "in" | "out" | "closed";

const statusOptions: { label: string; value: StatusFilter }[] = [
  { label: "Open", value: "open" },
  { label: "In range", value: "in" },
  { label: "Out of range", value: "out" },
  { label: "Closed", value: "closed" },
  { label: "All", value: "all" },
];

const matchesStatus = (p: PositionDto, f: StatusFilter) =>
  f === "all" ||
  (f === "closed"
    ? p.status === "Closed"
    : f === "open"
      ? p.status === "Open"
      : p.status === "Open" && p.range?.inRange === (f === "in"));

export function PositionsPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const [status, setStatus] = usePersistentState<StatusFilter>(
    // New key: the default changed to "open", and an old saved "all" would otherwise keep winning.
    "lpdash.positions.status",
    "open",
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
  const [sort, setSort] = usePersistentState<SortState>(
    "lpdash.portfolio.sort",
    { key: "created", dir: "desc" },
  );
  const totals = useSummary(owner, null).data;
  const positions = usePositions(owner, null);
  // Break-even comes from a separate, slower endpoint; the table renders without it and fills the column in when it arrives.
  const performance = usePerformance(owner);
  const breakEven = useMemo(
    () =>
      new Map<number, HodlBreakEvenDto>(
        (performance.data?.positions ?? []).flatMap((r) =>
          r.breakEven ? [[r.positionId, r.breakEven] as const] : [],
        ),
      ),
    [performance.data],
  );

  const protocols = useMemo(
    () => [...new Set((positions.data ?? []).map((p) => p.protocol))],
    [positions.data],
  );
  const visible = useMemo(
    () =>
      sortPositions(
        filterPositions(
          (positions.data ?? []).filter((p) => matchesStatus(p, status)),
          search,
          protocol,
          false,
        ),
        sort,
        { catchUpDays: new Map([...breakEven].map(([id, b]) => [id, b.daysToBreakEven])) },
      ),
    [positions.data, status, search, protocol, sort, breakEven],
  );
  const filtered = !!search.trim() || !!protocol || status !== "all";
  const toggleSort = (key: SortKey) =>
    setSort(
      sort.key === key
        ? { key, dir: sort.dir === "asc" ? "desc" : "asc" }
        : { key, dir: key === "position" ? "asc" : "desc" },
    );

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Segmented
          label="Status"
          options={statusOptions}
          value={status}
          onChange={setStatus}
        />
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          <UpdatedAgo />
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
          <Empty>No positions in this view.</Empty>
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
          breakEven={breakEven}
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
        className={`uppercase hover:text-slate-50 ${active ? "text-slate-50" : ""}`}
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
  breakEven,
}: {
  positions: PositionDto[];
  inverted: boolean;
  sort: SortState;
  onSort: (key: SortKey) => void;
  breakEven: ReadonlyMap<number, HodlBreakEvenDto>;
}) {
  const drawer = usePositionDrawer();
  const sum = (pick: (m: PositionDto["metrics"]) => number) =>
    positions.reduce((total, p) => total + pick(p.metrics), 0);
  const maxUnclaimed = Math.max(
    0,
    ...positions.map(
      (p) => p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd,
    ),
  );

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800">
      <table className="w-full text-sm">
        <thead className="bg-slate-900 text-left text-xs uppercase text-slate-400">
          <tr>
            <th className="px-3 py-2 w-10 text-right">#</th>
            <SortHeader label="Position" k="position" sort={sort} onSort={onSort} />
            <th className="px-3 py-2 w-64">Range</th>
            <SortHeader label="Age" k="age" sort={sort} onSort={onSort} />
            <SortHeader label="Value" k="value" right sort={sort} onSort={onSort} />
            <SortHeader label="Fees" k="fees" right sort={sort} onSort={onSort} />
            <SortHeader label="Unclaimed" k="unclaimed" right sort={sort} onSort={onSort} />
            <SortHeader label="P/L" k="pnl" right sort={sort} onSort={onSort} />
            <SortHeader label="IL" k="il" right sort={sort} onSort={onSort} />
            <SortHeader label="vs HODL" k="vsHodl" right sort={sort} onSort={onSort} />
            <SortHeader label="Catches up" k="catchUp" right sort={sort} onSort={onSort} />
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
                <td
                  className="cursor-help px-3 py-2 num"
                  title={
                    `Created ${formatDate(p.openedAt)}` +
                    (p.closedAt ? `\nWithdrawn ${formatDate(p.closedAt)}` : "")
                  }
                >
                  {formatAge(m.ageDays)}
                </td>
                <td className="px-3 py-2 text-right num">
                  {formatUsd(m.currentValueUsd)}
                </td>
                <td className="px-3 py-2 text-right num">
                  {formatUsd(m.feesEarnedUsd)}
                </td>
                <td className="border-x border-sky-900 bg-sky-950/30 px-3 py-2 text-right num">
                  {m.uncollectedFeesUsd + m.pendingRewardsUsd > 0 ? (
                    <>
                      <div className="font-semibold text-sky-400">
                        {formatUsd(m.uncollectedFeesUsd + m.pendingRewardsUsd)}
                      </div>
                      <div className="ml-auto mt-1 w-24">
                        <SplitBar
                          fees={m.uncollectedFeesUsd}
                          rewards={m.pendingRewardsUsd}
                          fraction={
                            maxUnclaimed > 0
                              ? (m.uncollectedFeesUsd + m.pendingRewardsUsd) /
                                maxUnclaimed
                              : 0
                          }
                          className="h-1.5"
                        />
                      </div>
                      <div className="mt-0.5 text-[11px] text-slate-400">
                        {formatUsd(m.uncollectedFeesUsd)} fees ·{" "}
                        {formatUsd(m.pendingRewardsUsd)} rewards
                      </div>
                    </>
                  ) : (
                    <span className="text-slate-500">—</span>
                  )}
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
                {(() => {
                  const c = catchUpCell(breakEven.get(p.id));
                  return (
                    <td
                      className={`cursor-help whitespace-nowrap px-3 py-2 text-right num ${c.className}`}
                      title={c.title}
                    >
                      {c.text}
                    </td>
                  );
                })()}
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
                      {timeInRangeBand(p.timeInRange) && (
                        <div
                          className="text-[11px] text-amber-400/80"
                          title={timeInRangeSource(p.timeInRange)}
                        >
                          {timeInRangeBand(p.timeInRange)}
                        </div>
                      )}
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
            <td colSpan={4} className="px-3 py-2 text-slate-400">
              Total · {positions.length}{" "}
              {positions.length === 1 ? "position" : "positions"}
            </td>
            <td className="px-3 py-2 text-right num">
              {formatUsd(sum((m) => m.currentValueUsd))}
            </td>
            <td className="px-3 py-2 text-right num">
              {formatUsd(sum((m) => m.feesEarnedUsd))}
            </td>
            <td className="border-x border-sky-900 bg-sky-950/30 px-3 py-2 text-right num">
              <div className="font-semibold text-sky-400">
                {formatUsd(
                  sum((m) => m.uncollectedFeesUsd + m.pendingRewardsUsd),
                )}
              </div>
              <div className="text-[11px] font-normal text-slate-400">
                {formatUsd(sum((m) => m.uncollectedFeesUsd))} fees ·{" "}
                {formatUsd(sum((m) => m.pendingRewardsUsd))} rewards
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
            <td colSpan={3} className="px-3 py-2" />
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
