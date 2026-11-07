import type { EarningsAlert } from "../../lib/earnings";
import { usePositionDrawer } from "../../lib/positionDrawer";

const chip: Record<EarningsAlert["severity"], string> = {
  warn: "bg-amber-500/15 text-amber-300",
  info: "bg-sky-500/15 text-sky-300",
};

/** Things that look off, shown only when there are any. Nothing renders on a quiet day. */
export function EarningsAlerts({ alerts }: { alerts: EarningsAlert[] }) {
  const drawer = usePositionDrawer();
  if (!alerts.length) return null;

  return (
    <ul className="space-y-1.5" aria-label="Needs attention">
      {alerts.map((a) => (
        <li
          key={a.id}
          className="flex items-start gap-2.5 rounded-lg border border-slate-800 bg-slate-900 px-3 py-2 text-sm"
        >
          <span
            className={`mt-0.5 shrink-0 rounded-full px-2 text-[11px] font-semibold uppercase tracking-wide ${chip[a.severity]}`}
          >
            {a.label}
          </span>
          <span className="min-w-0 flex-1">{a.text}</span>
          {a.positionId != null && (
            <button
              type="button"
              className="shrink-0 text-xs text-sky-400 hover:underline"
              onClick={() => drawer.open(a.positionId!)}
            >
              Open position →
            </button>
          )}
        </li>
      ))}
    </ul>
  );
}
