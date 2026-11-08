import { useMemo } from "react";
import {
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceDot,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useCollateralHistory } from "../../api/client";
import type { LoanDto, TrendEventDto } from "../../api/types";
import { shortDay } from "../../lib/borrow";
import { weekdayDate } from "../../lib/dates";
import { chart as theme } from "../../lib/chartTheme";
import { formatUsd } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { ErrorText, Panel, Segmented, Skeleton } from "../ui";
import { Delta } from "./bits";

const tip = { background: theme.tooltip.background, border: theme.tooltip.border };
const ROSE = "var(--color-rose-400)";

const kUsd = (v: number) => `$${(v / 1000).toFixed(1)}k`;

const EVENT_TEXT: Record<TrendEventDto["kind"], string> = {
  CollateralAdded: "Collateral added",
  CollateralRemoved: "Collateral withdrawn",
  Borrowed: "Borrowed more",
  Repaid: "Repaid or debt restated",
};

/** Loan choices for the collateral views: everything together, or one loan with collateral data. */
export function loanOptions(loans: LoanDto[]) {
  const manual = loans.filter((l) => l.kind === "manual" && l.collateralRatio != null);
  const aave = loans.some((l) => l.kind === "aave");
  // One loan on its own is the same view as "All loans", so only offer the split when there is more than one thing to pick.
  const options = [
    ...(aave ? [{ label: "Aave", value: "aave" }] : []),
    ...manual.map((l) => ({ label: l.label, value: l.key })),
  ];
  return options.length > 1 ? [{ label: "All loans", value: "all" }, ...options] : [{ label: "All loans", value: "all" }];
}

/** Is collateral or debt going up or down? Tiles, a collateral and debt chart with transaction markers, and the ratio below. */
export function CollateralSection({
  days,
  loan,
  onLoan,
  loans,
}: {
  days: number;
  loan: string;
  onLoan: (loan: string) => void;
  loans: LoanDto[];
}) {
  const { owner } = useOwner();
  const history = useCollateralHistory(owner, days, loan);
  const data = history.data;

  const view = useMemo(() => {
    if (!data || data.points.length < 2) return null;
    const first = data.points[0];
    const last = data.points[data.points.length - 1];
    return { first, last };
  }, [data]);

  // Events by day, so the tooltip can list what happened on the day being hovered.
  const eventsByDate = useMemo(() => {
    const map = new Map<string, TrendEventDto[]>();
    for (const e of data?.events ?? []) map.set(e.date, [...(map.get(e.date) ?? []), e]);
    return map;
  }, [data]);

  const options = loanOptions(loans);

  return (
    <Panel
      title="Collateral and debt over time"
      actions={options.length > 1 ? <Segmented label="Loans shown" options={options} value={loan} onChange={onLoan} /> : undefined}
    >
      {history.error ? (
        <ErrorText error={history.error} />
      ) : !data || !view ? (
        history.isLoading ? <Skeleton className="h-64 w-full" /> : <p className="text-sm text-slate-500">No collateral data yet. Add collateral amounts to a manual loan to chart it.</p>
      ) : (
        <>
          <div className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Tile label="Collateral" value={formatUsd(view.last.collateralUsd)}>
              <Delta value={view.last.collateralUsd - view.first.collateralUsd} goodUp text={`${formatUsd(Math.abs(view.last.collateralUsd - view.first.collateralUsd))} in ${days}d`} />
            </Tile>
            <Tile label="Debt" value={formatUsd(view.last.debtUsd)}>
              <Delta value={view.last.debtUsd - view.first.debtUsd} goodUp={false} text={`${formatUsd(Math.abs(view.last.debtUsd - view.first.debtUsd))} in ${days}d`} />
            </Tile>
            <Tile label="Collateral ÷ debt" value={view.last.ratio == null ? "—" : `${view.last.ratio.toFixed(2)}×`}>
              {view.last.ratio != null && view.first.ratio != null && (
                <Delta value={view.last.ratio - view.first.ratio} goodUp text={Math.abs(view.last.ratio - view.first.ratio).toFixed(2)} />
              )}
              {view.last.ltvPercent != null && (
                <span className="text-slate-500">
                  {" "}· LTV {view.last.ltvPercent.toFixed(0)}%{view.first.ltvPercent != null && ` (was ${view.first.ltvPercent.toFixed(0)}%)`}
                </span>
              )}
            </Tile>
          </div>

          <div className="mb-1 flex flex-wrap gap-x-4 text-xs text-slate-400">
            <Key color="var(--chart-a)">Collateral</Key>
            <Key color={ROSE}>Debt</Key>
            <span>○ Deposit, withdrawal, borrow or repay (hover)</span>
          </div>
          <div className="h-60">
            <ResponsiveContainer>
              <ComposedChart data={data.points}>
                <CartesianGrid stroke={theme.grid} vertical={false} />
                <XAxis dataKey="date" stroke={theme.axis} fontSize={11} tickFormatter={shortDay} minTickGap={28} />
                <YAxis stroke={theme.axis} fontSize={11} tickFormatter={kUsd} width={56} domain={["auto", "auto"]} />
                <Tooltip content={<TrendTooltip eventsByDate={eventsByDate} />} />
                <Line dataKey="collateralUsd" name="Collateral" stroke={theme.a} strokeWidth={2} dot={false} />
                <Line dataKey="debtUsd" name="Debt" stroke={ROSE} strokeWidth={2} dot={false} />
                {data.events.map((e, i) => {
                  const p = data.points.find((x) => x.date === e.date);
                  if (!p) return null;
                  const collateral = e.kind === "CollateralAdded" || e.kind === "CollateralRemoved";
                  return (
                    <ReferenceDot
                      key={i}
                      x={e.date}
                      y={collateral ? p.collateralUsd : p.debtUsd}
                      r={5}
                      fill="var(--chart-tip-bg)"
                      stroke={collateral ? theme.a : ROSE}
                      strokeWidth={2}
                    />
                  );
                })}
              </ComposedChart>
            </ResponsiveContainer>
          </div>
          <div className="mb-1 mt-4 flex flex-wrap items-baseline justify-between gap-2 text-xs">
            <span className="uppercase tracking-[.05em] text-slate-400">Collateral ÷ debt</span>
            <span className="text-slate-500">
              {data.liquidationRatio != null ? "Higher is safer. Dashed lines are your warn and liquidation levels." : "Pick one loan to see its warn and liquidation lines."}
            </span>
          </div>
          <div className="h-36">
            <ResponsiveContainer>
              <ComposedChart data={data.points}>
                <CartesianGrid stroke={theme.grid} vertical={false} />
                <XAxis dataKey="date" stroke={theme.axis} fontSize={11} tickFormatter={shortDay} minTickGap={28} />
                <YAxis stroke={theme.axis} fontSize={11} tickFormatter={(v) => `${Number(v).toFixed(1)}×`} width={44} domain={["auto", "auto"]} />
                <Tooltip contentStyle={tip} labelFormatter={(d) => weekdayDate(String(d))} formatter={(v) => [`${Number(v).toFixed(2)}×`, "Collateral ÷ debt"]} />
                {data.warnRatio != null && <ReferenceLine y={data.warnRatio} stroke="var(--color-amber-400)" strokeDasharray="4 3" label={{ value: `warn ${data.warnRatio.toFixed(2)}×`, fill: "var(--color-amber-400)", fontSize: 10, position: "insideTopRight" }} />}
                {data.liquidationRatio != null && <ReferenceLine y={data.liquidationRatio} stroke={ROSE} strokeDasharray="4 3" label={{ value: `liquidation ${data.liquidationRatio.toFixed(2)}×`, fill: ROSE, fontSize: 10, position: "insideBottomRight" }} />}
                <Line dataKey="ratio" name="Collateral ÷ debt" stroke="var(--color-slate-100)" strokeWidth={2} dot={false} />
              </ComposedChart>
            </ResponsiveContainer>
          </div>
          {data.excludedLoans.length > 0 && (
            <p className="mt-2 text-xs text-slate-500">Left out for lack of collateral data: {data.excludedLoans.join(", ")}.</p>
          )}
        </>
      )}
    </Panel>
  );
}

/** Tooltip for the collateral and debt chart: the two values for the day, plus anything that happened on it. */
function TrendTooltip({
  active,
  label,
  payload,
  eventsByDate,
}: {
  active?: boolean;
  label?: string | number;
  payload?: { name?: string; value?: number | string; color?: string }[];
  eventsByDate: Map<string, TrendEventDto[]>;
}) {
  if (!active || !payload?.length) return null;
  const events = eventsByDate.get(String(label)) ?? [];
  return (
    <div className="rounded-md px-3 py-2 text-xs shadow-lg" style={tip}>
      <div className="mb-1 text-slate-300">{weekdayDate(String(label))}</div>
      {payload.map((p) => (
        <div key={p.name} className="flex items-center justify-between gap-4" style={{ color: p.color }}>
          <span>{p.name}</span>
          <span className="num">{formatUsd(Number(p.value))}</span>
        </div>
      ))}
      {events.length > 0 && (
        <div className="mt-1.5 space-y-0.5 border-t border-slate-700 pt-1.5 text-slate-400">
          {events.map((e, i) => (
            <div key={i}>
              {EVENT_TEXT[e.kind]} on {e.label}: <span className="num text-slate-200">{formatUsd(e.usd, true)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Tile({ label, value, children }: { label: string; value: string; children?: React.ReactNode }) {
  return (
    <div className="rounded-md border border-slate-800 bg-slate-800/40 px-3 py-2.5">
      <div className="text-xs uppercase tracking-[.05em] text-slate-400">{label}</div>
      <div className="mt-0.5 text-xl font-medium num">{value}</div>
      <div className="text-xs">{children}</div>
    </div>
  );
}

function Key({ color, children }: { color: string; children: string }) {
  return (
    <span>
      <span className="mr-1.5 inline-block h-2 w-2 rounded-sm" style={{ background: color }} />
      {children}
    </span>
  );
}
