import { Link } from "react-router-dom";
import { useMarketWatch } from "../../api/market";
import { useOwner } from "../../lib/owner";
import { useHideBalances } from "../../lib/privacy";
import {
  TAG_CLASS,
  TAG_LABEL,
  TAG_TOOLTIP,
  countdownLabel,
  eventFigures,
  nextHighEvents,
  recentHighResult,
} from "../../lib/market";
import { useNow } from "../../lib/useNow";
import { Chip } from "../borrow/bits";
import { Skeleton } from "../ui";
import { LoanBufferBlock, LoanStressTable } from "./LoanStress";

/**
 * The Overview card: the next high-impact event with a countdown (or the result that just came out), the loan buffer and
 * what a price fall would do to the loan. The full calendar and headlines are on the Market watch page.
 */
export function NextUpCard() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const now = useNow(30_000);
  const watch = useMarketWatch(owner, 7);
  const data = watch.data;

  if (watch.isLoading) return <Skeleton className="h-40 w-full rounded-lg" />;
  if (watch.error || !data) {
    return <p className="rounded-lg border border-line bg-panel px-4 py-3 text-sm text-muted">Market watch is unavailable right now.</p>;
  }
  if (data.sources.calendar === "unavailable" && data.upcoming.length === 0 && data.happened.length === 0) {
    return (
      <p className="rounded-lg border border-line bg-panel px-4 py-3 text-sm text-muted">
        The market calendar is unavailable right now.{" "}
        <Link to="/market" className="text-accent hover:underline">
          Open Market watch
        </Link>
      </p>
    );
  }

  const recent = recentHighResult(data.happened, now);
  const next = nextHighEvents(data.upcoming)[0] ?? null;
  const featured = recent ?? next;
  const others = data.upcoming.filter((e) => e !== featured);
  const figures = featured ? eventFigures(featured) : [];

  return (
    <section className="rounded-lg border border-line bg-panel p-4" aria-label="Market watch">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-medium text-fg-2">Market watch</h2>
        <Link to="/market" className="text-sm text-accent hover:underline">
          Open Market watch
        </Link>
      </div>

      <div className="grid gap-5 md:grid-cols-[1.2fr_1fr_1.3fr]">
        <div className="min-w-0">
          {featured ? (
            <>
              <div className="text-xs uppercase tracking-[.05em] text-muted">{recent ? "Just happened" : "Next high-impact event"}</div>
              <div className="mt-1 text-lg font-semibold tracking-tight">{featured.name}</div>
              <div className="text-xs text-faint">
                {new Date(featured.at).toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" })}
                {" · "}
                {featured.timing ?? new Date(featured.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                {figures.length > 0 && ` · ${figures.map((f) => `${f.label.toLowerCase()} ${f.value}`).join(" · ")}`}
              </div>
              {recent ? (
                <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                  {recent.tag ? (
                    <span title={TAG_TOOLTIP}>
                      <Chip className={TAG_CLASS[recent.tag]}>{TAG_LABEL[recent.tag]}</Chip>
                    </span>
                  ) : (
                    <span className="text-muted">Reported</span>
                  )}
                  {next && (
                    <span className="text-xs text-muted">
                      Next: {next.name} in {countdownLabel(new Date(next.at).getTime(), now)}
                    </span>
                  )}
                </div>
              ) : (
                <div className="num mt-2 text-3xl font-medium tracking-tight">
                  {countdownLabel(new Date(featured.at).getTime(), now)}
                </div>
              )}
            </>
          ) : (
            <p className="text-sm text-muted">No high-impact events in the next 7 days.</p>
          )}
        </div>

        {data.loan ? (
          <>
            <LoanBufferBlock stress={data.loan.stress} />
            <LoanStressTable stress={data.loan.stress} />
          </>
        ) : (
          <p className="text-sm text-muted md:col-span-2">
            No loan to stress.{" "}
            <Link to="/borrow" className="text-accent hover:underline">
              Set up loans on Borrow
            </Link>{" "}
            to see how a price fall would affect it.
          </p>
        )}
      </div>

      {others.length > 0 && (
        <div className="mt-3 border-t border-line pt-2.5 text-xs text-muted">
          Then: {others.slice(0, 2).map((e) => e.name).join(" · ")}
          {others.length > 2 && ` · ${others.length - 2} more in 7 days`}
        </div>
      )}
    </section>
  );
}
