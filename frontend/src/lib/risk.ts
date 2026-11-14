import type { RiskFindingDto, RiskSummaryDto, VerdictLevel } from "../api/types";

/** The word on the badge. Colour is never the only signal. */
export function verdictLabel(level: VerdictLevel | null | undefined, run = 0, total = 0): string {
  switch (level) {
    case "Green":
      return "Green";
    case "Yellow":
      return "Yellow";
    case "Red":
      return "Red";
    case "Partial":
      return total > 0 ? `Partial ${run}/${total}` : "Partial";
    default:
      return "Not checked";
  }
}

export const verdictDot: Record<VerdictLevel, string> = {
  Green: "bg-up-fill",
  Yellow: "bg-warn-fill",
  Red: "bg-down-fill",
  Partial: "bg-muted",
  NotChecked: "bg-muted",
};

export const verdictText: Record<VerdictLevel, string> = {
  Green: "text-up",
  Yellow: "text-warn",
  Red: "text-down",
  Partial: "text-fg-2",
  NotChecked: "text-faint",
};

/** Higher is riskier: Red first when sorting "worst first". A pool nobody checked sorts below one that is Partial. */
export const verdictRisk = (level: VerdictLevel | null | undefined): number => {
  switch (level) {
    case "Red":
      return 4;
    case "Yellow":
      return 3;
    case "Partial":
      return 2;
    case "Green":
      return 0;
    default:
      return 1;
  }
};

/** What the Pools filter offers. */
export type RiskFilter = "any" | "noRed" | "Green" | "Yellow" | "Red" | "unchecked";

export const riskFilterLabels: Record<RiskFilter, string> = {
  any: "Any risk",
  noRed: "Hide red",
  Green: "Green only",
  Yellow: "Yellow only",
  Red: "Red only",
  unchecked: "Not checked",
};

export function matchesRiskFilter(summary: RiskSummaryDto | null | undefined, filter: RiskFilter): boolean {
  const level = summary?.level ?? "NotChecked";
  switch (filter) {
    case "any":
      return true;
    case "noRed":
      return level !== "Red";
    case "unchecked":
      return level === "NotChecked" || level === "Partial";
    default:
      return level === filter;
  }
}

const severityOrder = { Red: 0, Yellow: 1, Info: 2 } as const;

/** Findings grouped for the report: Red, then Yellow, then Info. */
export function groupFindings(findings: RiskFindingDto[]) {
  return (["Red", "Yellow", "Info"] as const)
    .map((severity) => ({ severity, items: findings.filter((f) => f.severity === severity) }))
    .filter((g) => g.items.length > 0)
    .sort((a, b) => severityOrder[a.severity] - severityOrder[b.severity]);
}

/** A pasted token address, trimmed and lower-cased; null when it is not 0x plus 40 hex characters. */
export function parseAddress(text: string): string | null {
  const value = text.trim();
  return /^0x[0-9a-fA-F]{40}$/.test(value) ? value.toLowerCase() : null;
}

/** Seconds until an ISO time, rounded up; 0 when it has passed. */
export const secondsUntil = (iso: string | null | undefined, now = Date.now()) =>
  iso ? Math.max(0, Math.ceil((new Date(iso).getTime() - now) / 1000)) : 0;
