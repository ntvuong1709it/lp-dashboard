import type { MarketEventDto } from "../../api/types";
import { IMPACT_CLASS, TAG_CLASS, TAG_LABEL, TAG_TOOLTIP, eventFigures, eventTimeLabel, groupByDay } from "../../lib/market";
import { Chip } from "../borrow/bits";

function EventRow({ event: e, happened }: { event: MarketEventDto; happened: boolean }) {
  const figures = eventFigures(e);
  return (
    <div className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-x-3 gap-y-1 border-t border-line py-2.5 sm:grid-cols-[4.5rem_minmax(0,1fr)_auto]">
      <span className="num pt-0.5 text-xs text-muted">{eventTimeLabel(e)}</span>
      <div className="min-w-0">
        <div className="font-medium">{e.name}</div>
        {figures.length > 0 && (
          <div className="mt-0.5 text-xs text-muted">
            {figures.map((f, i) => (
              <span key={f.label}>
                {i > 0 && " · "}
                {f.label} <span className="font-medium text-fg">{f.value}</span>
              </span>
            ))}
          </div>
        )}
        {e.reason && <div className="mt-0.5 text-xs text-muted">{e.reason}</div>}
        {e.category === "Earnings" && happened && (
          <div className="mt-0.5 text-xs text-muted">Reported. The source gives no result, so there is no tag.</div>
        )}
      </div>
      <div className="col-start-2 flex flex-wrap items-start gap-1.5 sm:col-start-auto sm:justify-end">
        {e.tag && (
          <span title={TAG_TOOLTIP}>
            <Chip className={TAG_CLASS[e.tag]}>{TAG_LABEL[e.tag]}</Chip>
          </span>
        )}
        <Chip className={IMPACT_CLASS[e.impact]}>{e.impact}</Chip>
        <span className="rounded border border-line-strong px-1.5 text-[11px] text-muted">{e.category}</span>
      </div>
    </div>
  );
}

/** Events grouped by day, in the order given (the server sorts them). */
export function EventList({ events, empty, happened = false }: { events: MarketEventDto[]; empty: string; happened?: boolean }) {
  const groups = groupByDay(events);
  if (!groups.length) return <div className="py-3 text-sm text-faint">{empty}</div>;
  return (
    <div>
      {groups.map((g) => (
        <div key={g.day}>
          <div className="mb-0.5 mt-4 text-xs uppercase tracking-[.05em] text-muted first:mt-0">{g.day}</div>
          {g.events.map((e) => (
            <EventRow key={e.id} event={e} happened={happened} />
          ))}
        </div>
      ))}
    </div>
  );
}
