import type { PositionDto, Protocol } from "../api/types";
import { protocolLabels } from "./format";

export type SortKey =
  | "position"
  | "age"
  | "created"
  | "closed"
  | "value"
  | "fees"
  | "pnl"
  | "il"
  | "vsHodl"
  | "apr";

export interface SortState {
  key: SortKey;
  dir: "asc" | "desc";
}

const pairName = (p: PositionDto) =>
  `${p.token0?.symbol ?? "?"}/${p.token1?.symbol ?? "?"}`;

const time = (iso: string | null) => (iso ? Date.parse(iso) : null);

const sortValue: Record<SortKey, (p: PositionDto) => number | string | null> = {
  position: pairName,
  age: (p) => p.metrics.ageDays,
  created: (p) => time(p.openedAt),
  closed: (p) => time(p.closedAt),
  value: (p) => p.metrics.currentValueUsd,
  fees: (p) => p.metrics.feesEarnedUsd,
  pnl: (p) => p.metrics.pnlUsd,
  il: (p) => p.metrics.impermanentLossUsd,
  vsHodl: (p) => p.metrics.pnlVsHodlUsd,
  apr: (p) => p.metrics.feeAprPercent,
};

/** Rows with no value for the sort key always go last, regardless of direction. */
export function sortPositions(
  positions: PositionDto[],
  { key, dir }: SortState,
): PositionDto[] {
  const get = sortValue[key];
  const sign = dir === "asc" ? 1 : -1;
  return [...positions].sort((a, b) => {
    const x = get(a);
    const y = get(b);
    if (x == null && y == null) return 0;
    if (x == null) return 1;
    if (y == null) return -1;
    if (typeof x === "string" || typeof y === "string") {
      return sign * String(x).localeCompare(String(y));
    }
    return sign * (x - y);
  });
}

export function filterPositions(
  positions: PositionDto[],
  search: string,
  protocol: Protocol | "",
  inRangeOnly: boolean,
): PositionDto[] {
  const terms = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return positions.filter((p) => {
    if (protocol && p.protocol !== protocol) return false;
    // Closed positions have no meaningful "in range" state.
    if (inRangeOnly && !(p.status === "Open" && p.range?.inRange)) return false;
    if (terms.length === 0) return true;
    const haystack = [
      pairName(p),
      p.token0?.symbol,
      p.token1?.symbol,
      p.tokenId,
      p.poolAddress,
      protocolLabels[p.protocol],
    ]
      .join(" ")
      .toLowerCase();
    // "#123" matches token ids only; everything else is a substring match.
    return terms.every((t) =>
      t.startsWith("#")
        ? p.tokenId.startsWith(t.slice(1))
        : haystack.includes(t),
    );
  });
}
