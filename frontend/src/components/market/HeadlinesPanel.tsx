import type { HeadlineDto } from "../../api/types";
import { formatAgo } from "../../lib/format";
import { ExternalLink, Panel } from "../ui";

/** The newest headlines, untagged: only scheduled results get a risk tag. */
export function HeadlinesPanel({ headlines, now, unavailable }: { headlines: HeadlineDto[]; now: number; unavailable: boolean }) {
  return (
    <Panel title="Latest headlines">
      {headlines.length === 0 ? (
        <div className="text-sm text-faint">{unavailable ? "Headlines are unavailable right now." : "No headlines."}</div>
      ) : (
        <ul>
          {headlines.map((h) => (
            <li key={h.url} className="border-t border-line py-2 first:border-t-0 first:pt-0 last:pb-0">
              <ExternalLink href={h.url}>
                {h.title}
              </ExternalLink>
              <div className="mt-0.5 text-xs text-muted">
                {h.source} · {formatAgo(h.publishedAt, now)}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
