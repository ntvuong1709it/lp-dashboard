import { useState } from "react";
import type { HeadlineDto } from "../../api/types";
import { formatAgo } from "../../lib/format";
import { ExternalLink, Panel } from "../ui";

/** Up to two letters from the source name: "Federal Reserve" becomes "FR", "CoinDesk" becomes "C". */
function initials(source: string): string {
  const words = source.split(/\s+/).filter(Boolean);
  return words
    .slice(0, 2)
    .map((w) => w[0]!.toUpperCase())
    .join("");
}

/** The story's picture, or a dashed tile with the source initials when the feed has none or it will not load. */
function Thumb({ headline }: { headline: HeadlineDto }) {
  const [failed, setFailed] = useState(false);
  const box = "h-12 w-[72px] shrink-0 rounded-md border";
  if (headline.imageUrl && !failed) {
    return (
      <img
        src={headline.imageUrl}
        alt=""
        loading="lazy"
        referrerPolicy="no-referrer"
        onError={() => setFailed(true)}
        className={`${box} border-line bg-raised object-cover`}
      />
    );
  }
  return (
    <div aria-hidden="true" className={`${box} grid place-items-center border-dashed border-line-strong text-sm font-semibold text-muted`}>
      {initials(headline.source)}
    </div>
  );
}

/** The newest headlines, untagged: only scheduled results get a risk tag. */
export function HeadlinesPanel({ headlines, now, unavailable }: { headlines: HeadlineDto[]; now: number; unavailable: boolean }) {
  return (
    <Panel title="Latest headlines">
      {headlines.length === 0 ? (
        <div className="text-sm text-faint">{unavailable ? "Headlines are unavailable right now." : "No headlines."}</div>
      ) : (
        <ul>
          {headlines.map((h) => (
            <li key={h.url} className="flex items-start gap-3 border-t border-line py-2.5 first:border-t-0 first:pt-0 last:pb-0">
              <Thumb headline={h} />
              <div className="min-w-0 flex-1">
                <ExternalLink href={h.url}>
                  {h.title}
                </ExternalLink>
                <div className="mt-0.5 text-xs text-muted">
                  {h.source} · {formatAgo(h.publishedAt, now)}
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
