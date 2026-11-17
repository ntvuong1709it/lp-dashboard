import type { MarketEventDto } from "../../api/types";
import { TAG_CLASS, TAG_TOOLTIP, dayLabel, eventFigures, eventTimeLabel, groupByDay, tagLabel, whyItMatters } from "../../lib/market";
import { Chip } from "../borrow/bits";

function ImpactDot({ impact }: { impact: MarketEventDto["impact"] }) {
  return impact === "High" ? (
    <span title="Big event" className="inline-block h-2 w-2 rounded-full bg-down-fill" />
  ) : (
    <span title="Smaller event" className="inline-block h-2 w-2 rounded-full border-[1.5px] border-warn-fill" />
  );
}

function EventRow({ event: e, happened, open }: { event: MarketEventDto; happened: boolean; open: boolean }) {
  const figures = eventFigures(e);
  const label = tagLabel(e);
  const why = whyItMatters(e);
  const note = happened ? (e.reason ?? (e.category === "Earnings" ? "Reported. The source gives no result, so there is no tag." : null)) : null;
  return (
    <details open={open} className="group border-t border-line">
      <summary className="grid cursor-pointer list-none grid-cols-[4.6rem_minmax(0,1fr)_auto] items-baseline gap-x-3 gap-y-1 py-3 [&::-webkit-details-marker]:hidden">
        <span className="num text-xs text-muted">{eventTimeLabel(e)}</span>
        <span className="min-w-0">
          <span className="block font-semibold group-hover:text-accent">{e.name}</span>
          {figures.length > 0 && (
            <span className="mt-0.5 block text-xs text-muted">
              {figures.map((f, i) => (
                <span key={f.label}>
                  {i > 0 && " · "}
                  {f.label} <span className="num font-medium text-fg">{f.value}</span>
                </span>
              ))}
            </span>
          )}
        </span>
        <span className="flex items-center justify-end gap-2">
          {e.tag && label && (
            <span title={TAG_TOOLTIP}>
              <Chip className={TAG_CLASS[e.tag]}>{label}</Chip>
            </span>
          )}
          {e.category === "Earnings" && <span className="rounded border border-line-strong px-1.5 text-[11px] text-muted">Earnings</span>}
          <ImpactDot impact={e.impact} />
          <i
            aria-hidden="true"
            className="mb-0.5 inline-block h-2 w-2 rotate-45 border-b-[1.5px] border-r-[1.5px] border-faint transition-transform group-open:-mb-0.5 group-open:rotate-[225deg] motion-reduce:transition-none"
          />
        </span>
      </summary>
      <div className="max-w-prose pb-4 pl-[calc(4.6rem+0.75rem)] text-sm text-fg-2">
        {note && <p>{note}</p>}
        {why && <p className={note ? "mt-1.5 text-xs text-faint" : ""}>{why}</p>}
        {!note && !why && <p className="text-faint">No further notes for this event.</p>}
      </div>
    </details>
  );
}

/** Events grouped by day, in the order given (the server sorts them). The first event with `openId` starts open. */
export function EventList({
  events,
  empty,
  happened = false,
  openId = null,
  today,
}: {
  events: MarketEventDto[];
  empty: string;
  happened?: boolean;
  openId?: string | null;
  today: string;
}) {
  const groups = groupByDay(events);
  if (!groups.length) return <div className="py-3 text-sm text-faint">{empty}</div>;
  return (
    <div>
      {groups.map((g) => (
        <div key={g.key}>
          <h3 className="mb-0.5 mt-5 font-display text-sm font-semibold text-fg-2 first:mt-0">
            {dayLabel(g.date)}
            {g.key === today && <span className="ml-2 font-sans text-xs font-normal text-faint">today</span>}
          </h3>
          {g.events.map((e) => (
            <EventRow key={e.id} event={e} happened={happened} open={e.id === openId} />
          ))}
        </div>
      ))}
    </div>
  );
}
