import { ApiError } from "../../api/http";
import { useRecheckToken } from "../../api/risk";
import type { RiskFindingDto, RiskSourceDto, TokenRiskDto } from "../../api/types";
import { formatAgo, formatDate, shortAddress } from "../../lib/format";
import { groupFindings, secondsUntil, verdictDot, verdictLabel, verdictText } from "../../lib/risk";
import { useNow } from "../../lib/useNow";
import { CopyButton, ExternalLink } from "../ui";

const severityStyle = {
  Red: { dot: "bg-down-fill", title: "Red flags", text: "text-down" },
  Yellow: { dot: "bg-warn-fill", title: "Yellow flags", text: "text-warn" },
  Info: { dot: "bg-muted", title: "For information", text: "text-muted" },
} as const;

function Finding({ finding }: { finding: RiskFindingDto }) {
  const style = severityStyle[finding.severity];
  return (
    <li className="flex gap-2.5 py-2">
      <span aria-hidden="true" className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${style.dot}`} />
      <div className="min-w-0 text-sm">
        <div className="font-medium text-fg">{finding.title}</div>
        <div className="break-words text-muted">{finding.evidence}</div>
        <div className="mt-0.5 flex flex-wrap items-center gap-x-3 text-xs text-faint">
          <span>Source: {finding.source}</span>
          {finding.link && finding.subject && (
            <ExternalLink href={finding.link} title="Open the address on Basescan">
              {shortAddress(finding.subject)} on Basescan
            </ExternalLink>
          )}
        </div>
      </div>
    </li>
  );
}

export function FindingList({ findings }: { findings: RiskFindingDto[] }) {
  const groups = groupFindings(findings);
  if (groups.length === 0) return <p className="text-sm text-muted">No findings.</p>;
  return (
    <div className="space-y-3">
      {groups.map((g) => (
        <section key={g.severity} aria-label={severityStyle[g.severity].title}>
          <h4 className={`text-xs font-semibold uppercase tracking-wide ${severityStyle[g.severity].text}`}>
            {severityStyle[g.severity].title} · {g.items.length}
          </h4>
          <ul className="divide-y divide-line">
            {g.items.map((f, i) => (
              <Finding key={`${f.code}-${i}`} finding={f} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

/** Which checks ran and which did not, so a verdict is read together with what it was based on. */
export function SourceList({ sources }: { sources: RiskSourceDto[] }) {
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
      {sources.map((s) => (
        <li key={s.name} className={s.answered ? "text-muted" : "text-warn"} title={s.detail ?? undefined}>
          <span aria-hidden="true">{s.answered ? "✓" : "✗"}</span> {s.displayName}
          {!s.answered && s.detail ? ` (${s.detail})` : ""}
          {s.required ? "" : " · optional"}
        </li>
      ))}
    </ul>
  );
}

/** "Recheck" and the time left before the server allows another one; a refused re-check shows the server's message. */
export function RecheckButton({
  recheckAvailableAt,
  pending,
  onClick,
  error,
}: {
  recheckAvailableAt: string | null;
  pending: boolean;
  onClick: () => void;
  error: unknown;
}) {
  const now = useNow();
  const wait = secondsUntil(recheckAvailableAt, now);
  return (
    <span className="flex items-center gap-2">
      <button
        type="button"
        disabled={pending || wait > 0}
        onClick={onClick}
        title={wait > 0 ? "A check this recent is not repeated; the sources have limits." : "Ask every source again"}
        className="rounded-md border border-line-strong px-2 py-1 text-xs text-fg-2 hover:bg-raised disabled:cursor-not-allowed disabled:opacity-60"
      >
        {pending ? "Checking…" : wait > 0 ? `Recheck in ${wait >= 60 ? `${Math.ceil(wait / 60)} min` : `${wait} s`}` : "Recheck"}
      </button>
      {error instanceof ApiError && <span className="text-xs text-warn">{error.message}</span>}
    </span>
  );
}

/**
 * A token's verdict and the evidence behind it: findings grouped Red, Yellow, Info with their numbers and sources, the
 * checks that ran, when it was checked, and a Recheck button. "No flags" is never "safe", and the report says so.
 */
export function RiskReport({ report, compact = false }: { report: TokenRiskDto; compact?: boolean }) {
  const recheck = useRecheckToken(report.chain, report.address);
  const now = useNow(30_000);
  const links = report.links;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            {!compact && <span className="text-base font-semibold">{report.symbol ?? "Token"}</span>}
            <span className={`inline-flex items-center gap-1.5 text-sm font-semibold ${verdictText[report.level]}`}>
              <span aria-hidden="true" className={`inline-block h-2.5 w-2.5 rounded-full ${verdictDot[report.level]}`} />
              {verdictLabel(report.level, report.checksRun, report.checksTotal)}
            </span>
            {report.trusted && (
              <span className="rounded-full border border-line-strong px-1.5 text-[11px] text-muted" title="On the config list of well-known tokens: exempt from the owner, mint, blacklist and proxy rules">
                trusted token
              </span>
            )}
          </div>
          <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-faint">
            <span className="font-mono">{shortAddress(report.address)}</span>
            <CopyButton text={report.address} title="Copy the token address" />
            <span title={formatDate(report.checkedAt)}>checked {formatAgo(report.checkedAt, now)}</span>
          </div>
        </div>
        <RecheckButton
          recheckAvailableAt={recheck.data?.recheckAvailableAt ?? report.recheckAvailableAt}
          pending={recheck.isPending}
          onClick={() => recheck.mutate()}
          error={recheck.error}
        />
      </div>

      {report.level === "Partial" && (
        <p className="rounded-md border border-warn-fill/30 bg-warn-fill/10 px-3 py-2 text-xs text-warn">
          {report.checksRun} of {report.checksTotal} checks ran, so this cannot be called green. See which ones did not, below.
        </p>
      )}

      <FindingList findings={report.findings} />
      <SourceList sources={report.sources} />

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
        {links.explorer && <ExternalLink href={links.explorer}>Basescan</ExternalLink>}
        {links.honeypot && <ExternalLink href={links.honeypot}>honeypot.is</ExternalLink>}
        {links.dexScreener && <ExternalLink href={links.dexScreener}>DexScreener</ExternalLink>}
      </div>
      <p className="text-xs text-faint">No flags is not a guarantee of safety: two services reading the same contract can both miss a brand-new scam.</p>
    </div>
  );
}
