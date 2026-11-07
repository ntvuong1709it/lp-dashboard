import type { Headline } from "../../lib/earnings";
import { formatUsd } from "../../lib/format";

function Change({ h }: { h: Headline }) {
  if (!h.available) return <span>No tracked days yet</span>;
  if (h.changePct == null) return <span>Not enough history to compare yet</span>;
  const up = h.changePct >= 0;
  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      <span
        className={`rounded px-1.5 num ${up ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"}`}
      >
        {up ? "▲" : "▼"} {Math.abs(h.changePct).toFixed(0)}%
      </span>
      {h.changeLabel}
    </span>
  );
}

/** Fees (sky) and rewards (violet), the same colours as the chart below. */
function Split({ fees, rewards }: { fees: number; rewards: number }) {
  const total = fees + rewards;
  const feesShare = total > 0 ? (fees / total) * 100 : 0;
  return (
    <div
      className="flex h-1.5 overflow-hidden rounded-full bg-slate-800"
      title={`Swap fees ${formatUsd(fees)} · Rewards ${formatUsd(rewards)}`}
    >
      <div style={{ width: `${feesShare}%`, background: "var(--chart-a)" }} />
      <div style={{ width: `${100 - feesShare}%`, background: "var(--chart-d)" }} />
    </div>
  );
}

/** Yesterday, average per day, this week and this month, each against the period before it. */
export function HeadlineCards({ cards }: { cards: Headline[] }) {
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {cards.map((h) => (
        // Cards stretch to the tallest one and the split bar sits at the bottom, so the bars line up across the row.
        <div key={h.key} className="flex flex-col rounded-lg border border-slate-800 bg-slate-900 p-4">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">
            {h.label} · {h.aside}
          </div>
          <div className="mt-1 text-2xl font-medium tracking-tight num">{h.available ? formatUsd(h.total) : "—"}</div>
          <div className="mt-1 space-y-1 text-xs text-slate-500">
            <div>
              <Change h={h} />
            </div>
            {h.detail && <div>{h.detail}</div>}
          </div>
          {h.available && (
            <div className="mt-auto pt-3">
              <Split fees={h.fees} rewards={h.rewards} />
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
