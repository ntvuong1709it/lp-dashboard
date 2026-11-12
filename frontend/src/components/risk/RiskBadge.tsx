import type { RiskSummaryDto, VerdictLevel } from "../../api/types";
import { formatAgo } from "../../lib/format";
import { verdictDot, verdictLabel, verdictText } from "../../lib/risk";

/**
 * A dot and a word: Green, Yellow, Red, "Not checked" or "Partial 6/9". The word is always there, so colour is never the
 * only signal. The tooltip lists the worst findings. Summaries come from the cache; showing a badge never triggers a check.
 */
export function RiskBadge({
  summary,
  level,
  className = "",
}: {
  summary?: RiskSummaryDto | null;
  /** For callers that have a level and nothing else. */
  level?: VerdictLevel;
  className?: string;
}) {
  const shown: VerdictLevel = summary?.level ?? level ?? "NotChecked";
  const label = verdictLabel(shown, summary?.checksRun, summary?.checksTotal);

  const lines: string[] = [];
  if (shown === "NotChecked") lines.push("Not checked yet. Open the pool, or look the token up on the Risk page.");
  else if (summary?.top.length) lines.push(...summary.top);
  else if (shown === "Green") lines.push("No flags. That is not a guarantee of safety.");
  else if (shown === "Partial") lines.push("Some checks did not run, so this cannot be called green.");
  if (summary?.checkedAt) lines.push(`Checked ${formatAgo(summary.checkedAt)}`);

  return (
    <span className={`inline-flex items-center gap-1.5 whitespace-nowrap text-xs font-medium ${verdictText[shown]} ${className}`} title={lines.join("\n")}>
      <span aria-hidden="true" className={`inline-block h-2 w-2 rounded-full ${verdictDot[shown]}`} />
      {label}
    </span>
  );
}
