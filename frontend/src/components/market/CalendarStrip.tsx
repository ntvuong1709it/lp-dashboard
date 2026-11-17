import type { MarketEventDto } from "../../api/types";
import { shortName, stripDays } from "../../lib/market";
import { Panel, Segmented } from "../ui";

const WINDOW_OPTIONS = [
  { label: "7 days", value: 7 },
  { label: "14 days", value: 14 },
];

/**
 * Three days back and the window ahead, one column per day, with the events as short names. A quiet day looks quiet.
 * Pick a day to show only that day in the list.
 */
export function CalendarStrip({
  events,
  now,
  windowDays,
  onWindow,
  day,
  onDay,
}: {
  events: MarketEventDto[];
  now: number;
  windowDays: 7 | 14;
  onWindow: (n: 7 | 14) => void;
  day: string | null;
  onDay: (key: string | null) => void;
}) {
  const days = stripDays(events, windowDays, now);
  return (
    <Panel
      title="Calendar"
      actions={
        <span className="flex flex-wrap items-center gap-3">
          {day ? (
            <button type="button" className="text-xs font-medium text-accent hover:underline" onClick={() => onDay(null)}>
              Show every day
            </button>
          ) : (
            <span className="text-xs text-faint">Pick a day to see only that day</span>
          )}
          <Segmented label="Window" options={WINDOW_OPTIONS} value={windowDays} onChange={(n) => onWindow(n === 7 ? 7 : 14)} />
        </span>
      }
    >
      <div className="flex gap-1 overflow-x-auto pb-1" role="group" aria-label="Days with events">
        {days.map((d) => (
          <button
            key={d.key}
            type="button"
            aria-pressed={day === d.key}
            aria-label={`${d.date.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}, ${
              d.events.length === 0 ? "no events" : `${d.events.length} ${d.events.length === 1 ? "event" : "events"}`
            }${d.today ? ", today" : ""}`}
            onClick={() => onDay(day === d.key ? null : d.key)}
            className={`flex min-w-[3.4rem] flex-1 flex-col items-center gap-1 rounded-lg border px-1 pb-2 pt-1.5 text-center ${
              day === d.key ? "border-accent-fill bg-accent-fill/15" : "border-transparent hover:bg-raised"
            } ${d.past ? "opacity-50" : ""}`}
          >
            <span className="text-[11px] text-faint">{d.weekday}</span>
            <span
              className={`num grid h-6.5 min-w-6.5 place-items-center text-sm font-medium ${
                d.today ? "rounded-full bg-accent-solid text-white" : d.weekend ? "text-faint" : "text-fg-2"
              }`}
            >
              {d.dayOfMonth}
            </span>
            <span className="flex min-h-9 flex-col items-center gap-0.5">
              {d.events.length === 0 ? (
                <span aria-hidden="true" className="text-faint">
                  ·
                </span>
              ) : (
                d.events.slice(0, 3).map((e) => (
                  <span
                    key={e.id}
                    className={`flex max-w-[3.2rem] items-center gap-1 text-[10.5px] leading-tight ${e.impact === "High" ? "font-semibold text-fg" : "text-muted"}`}
                  >
                    <i
                      aria-hidden="true"
                      className={`h-1.75 w-1.75 shrink-0 rounded-full ${e.impact === "High" ? "bg-down-fill" : "border-[1.5px] border-warn-fill"}`}
                    />
                    <span className="truncate">{shortName(e)}</span>
                  </span>
                ))
              )}
              {d.events.length > 3 && <span className="text-[10px] text-faint">+{d.events.length - 3}</span>}
            </span>
          </button>
        ))}
      </div>
      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-faint">
        <span>
          <i className="mr-1.5 inline-block h-1.75 w-1.75 rounded-full bg-down-fill" />
          Big event
        </span>
        <span>
          <i className="mr-1.5 inline-block h-1.75 w-1.75 rounded-full border-[1.5px] border-warn-fill" />
          Smaller event
        </span>
        <span>Faded days are over</span>
      </div>
    </Panel>
  );
}
