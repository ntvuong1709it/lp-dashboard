import type { MarketWatchDto } from "../../api/types";
import {
  TAG_CLASS,
  TAG_TOOLTIP,
  countdownLabel,
  countdownParts,
  dayLabel,
  eventFigures,
  eventTimeLabel,
  nextHighEvents,
  recentHighResult,
  tagLabel,
  whyItMatters,
} from "../../lib/market";
import { Chip } from "../borrow/bits";
import { Panel } from "../ui";

/** The next big event as a countdown, why it matters, and what comes after it. A result from the last 12 hours is said first. */
export function NextUp({ data, now }: { data: MarketWatchDto; now: number }) {
  const upcoming = nextHighEvents(data.upcoming);
  const next = upcoming[0] ?? null;
  const recent = recentHighResult(data.happened, now);
  const then = upcoming.slice(1, 3);
  const why = next ? whyItMatters(next) : null;
  const recentLabel = recent ? tagLabel(recent) : null;
  // Earnings have no result in the source, only a forecast, so they are said as "reported" without numbers.
  const figures = recent && recent.category !== "Earnings" ? eventFigures(recent) : [];

  return (
    <Panel title="Next big event">
      {next ? (
        <>
          <div className="text-sm text-muted">
            {dayLabel(new Date(next.at))} · {eventTimeLabel(next)}
          </div>
          <h3 className="mt-1 font-display text-2xl font-bold leading-tight tracking-tight">{next.name}</h3>
          <div
            className="num mt-3 text-5xl font-medium leading-none tracking-tight"
            aria-label={`in ${countdownLabel(new Date(next.at).getTime(), now)}`}
          >
            {countdownParts(new Date(next.at).getTime(), now).map((p, i) => (
              <span key={p.unit}>
                {i > 0 && " "}
                {p.value}
                <small className="text-base text-muted">{p.unit}</small>
              </span>
            ))}
          </div>
          {why && (
            <p className="mt-4 max-w-prose border-t border-line pt-3 text-sm text-fg-2">
              <span className="text-muted">Why it matters.</span> {why}
            </p>
          )}
          {then.length > 0 && (
            <p className="mt-3 text-sm text-muted">
              After that:{" "}
              {then.map((e, i) => (
                <span key={e.id}>
                  {i > 0 && ", "}
                  <span className="font-medium text-fg-2">{e.name}</span> in {countdownLabel(new Date(e.at).getTime(), now)}
                </span>
              ))}
              .
            </p>
          )}
        </>
      ) : (
        <p className="py-2 text-sm text-muted">No big events in this window.</p>
      )}

      {recent && (
        <div className="mt-4 flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-md bg-raised px-3 py-2 text-sm text-fg-2">
          <span>
            {recent.category === "Earnings" ? "Reported:" : "Just out:"} <span className="font-medium text-fg">{recent.name}</span>
            {figures.length > 0 && <span className="text-muted"> ({figures.map((f) => `${f.label.toLowerCase()} ${f.value}`).join(", ")})</span>}
          </span>
          {recent.tag && recentLabel && (
            <span title={TAG_TOOLTIP}>
              <Chip className={TAG_CLASS[recent.tag]}>{recentLabel}</Chip>
            </span>
          )}
        </div>
      )}
    </Panel>
  );
}
