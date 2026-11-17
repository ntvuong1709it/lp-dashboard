import type { ReactNode } from "react";
import { PERIOD_COLUMNS, type Answer, type PayPart } from "../../lib/borrowAnswer";
import { formatPercent, formatUsd, pnlClass } from "../../lib/format";
import { Panel } from "../ui";

interface Row {
  key: string;
  label: string;
  sub: ReactNode;
  perDay: number;
  signed?: boolean;
  tone?: (v: number) => string;
  className?: string;
  /** A part of the row above it, drawn smaller and indented. */
  indent?: boolean;
}

/** What you pay and what comes in, for a day, week, month and year at once. Only LP income is the variable part. */
export function MoneyTable({ a, days, parts }: { a: Answer; days: number; parts: PayPart[] }) {
  const hasAave = a.aavePerDay > 0;
  // Only break "You pay" down when there is more than one group to tell apart.
  const split: Row[] =
    parts.length > 1
      ? parts.map((p) => ({
          key: `pay-${p.key}`,
          label: p.label,
          sub: "Part of what you pay",
          perDay: p.perDay,
          tone: () => "text-down",
          indent: true,
        }))
      : [];
  const rows: Row[] = [
    {
      key: "pay",
      label: "You pay",
      sub: (
        <>
          Interest on <span className="num">{formatUsd(a.debtUsd)}</span> at <span className="num">{formatPercent(a.payPer100)}</span>
          {a.basis === "past" && " on average"}
        </>
      ),
      perDay: a.payPerDay,
      tone: () => "text-down",
    },
    ...split,
    ...(hasAave
      ? [
          {
            key: "aave",
            label: "Aave yield, set by Aave",
            sub: "Supply yield on your collateral. It comes with the loan.",
            perDay: a.aavePerDay,
          },
          {
            key: "need",
            label: "LP must earn",
            sub: "You pay minus Aave yield. This is the part you work for.",
            perDay: a.needPerDay,
            className: "bg-raised/50 font-semibold",
          },
        ]
      : []),
    {
      key: "lp",
      label: "LP earns",
      sub: a.basis === "now" ? "Fees and rewards, last 7 days' pace" : `Fees and rewards, past ${days} days`,
      perDay: a.lpPerDay,
    },
    {
      key: "left",
      label: "Left over",
      sub: `LP earns minus ${hasAave ? "LP must earn" : "what you pay"}, before gas`,
      perDay: a.lpPerDay - a.needPerDay,
      signed: true,
      tone: pnlClass,
      className: "border-t-2 border-line-strong font-semibold",
    },
  ];

  return (
    <Panel title="What you pay and earn">
      <p className="mb-3 text-sm">
        {hasAave ? (
          <>
            Aave yield already covers <b className="num">{formatUsd(a.aavePerDay)}</b> of the <b className="num">{formatUsd(a.payPerDay)}</b> you pay each day.
            Your LP has to bring in the other <b className="num">{formatUsd(a.needPerDay)}</b> a day. It brings{" "}
            <b className={`num ${a.lpPerDay >= a.needPerDay ? "text-up" : "text-down"}`}>{formatUsd(a.lpPerDay)}</b>.
          </>
        ) : (
          <>
            You pay <b className="num">{formatUsd(a.payPerDay)}</b> a day. Your LP brings in{" "}
            <b className={`num ${a.lpPerDay >= a.payPerDay ? "text-up" : "text-down"}`}>{formatUsd(a.lpPerDay)}</b>.
          </>
        )}
      </p>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[34rem] text-sm">
          <thead>
            <tr className="text-xs uppercase tracking-[.04em] text-muted">
              <th className="px-2.5 py-1.5 text-left font-medium" />
              {PERIOD_COLUMNS.map((p) => (
                <th key={p.key} className="px-2.5 py-1.5 text-right font-medium">
                  {p.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.key} className={`border-t border-line ${r.className ?? ""}`}>
                <td className={r.indent ? "py-1.5 pl-6 pr-2.5" : "px-2.5 py-2.5"}>
                  <div className={r.indent ? "text-[13px]" : r.className?.includes("font-semibold") ? "" : "font-medium"}>{r.label}</div>
                  {!r.indent && <div className="text-xs font-normal text-muted">{r.sub}</div>}
                </td>
                {PERIOD_COLUMNS.map((p) => {
                  const v = r.perDay * p.factor;
                  return (
                    <td key={p.key} className={`px-2.5 text-right num ${r.indent ? "py-1.5 text-[13px]" : "py-2.5"} ${r.tone ? r.tone(v) : ""}`}>
                      {formatUsd(v, r.signed)}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-2 text-xs text-faint">A week is 7 days and a month a twelfth of the year. Aave rates float, so what you pay moves.</p>
    </Panel>
  );
}
