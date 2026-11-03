import type { PositionDto, RangeDto } from "../api/types";

/** Fraction of the range width within which an in-range position counts as "near the edge". */
const EDGE_FRACTION = 0.08;

/** Unclaimed fees + rewards (USD) above which a position is flagged for collecting. */
export const UNCLAIMED_ALERT_USD = 200;

export const pairLabel = (p: PositionDto) =>
  `${p.token0?.symbol ?? "?"}/${p.token1?.symbol ?? "?"}`;

/** True when the current price is inside the range but close to either bound. */
export function isNearEdge(range: RangeDto | null | undefined): boolean {
  if (!range || range.inRange !== true || range.priceCurrent == null) return false;
  const width = range.priceUpper - range.priceLower;
  if (width <= 0) return false;
  const pos = (range.priceCurrent - range.priceLower) / width;
  return pos < EDGE_FRACTION || pos > 1 - EDGE_FRACTION;
}

export type AlertKind = "out" | "edge" | "unclaimed" | "prices";

export interface Alert {
  kind: AlertKind;
  position: PositionDto;
  title: string;
  detail: string;
}

/** Things worth looking at among the open positions, most urgent first. */
export function buildAlerts(
  open: PositionDto[],
  usd: (n: number) => string,
  price: (n: number | null) => string,
): Alert[] {
  const alerts: Alert[] = [];
  for (const p of open) {
    const pair = pairLabel(p);
    const r = p.range;
    if (r?.inRange === false) {
      const above = r.priceCurrent != null && r.priceCurrent > r.priceUpper;
      alerts.push({
        kind: "out",
        position: p,
        title: `${pair} #${p.tokenId} is out of range and earning no fees.`,
        detail: `Price ${price(r.priceCurrent)} is ${above ? "above" : "below"} the ${price(r.priceLower)} – ${price(r.priceUpper)} range.`,
      });
    } else if (r && isNearEdge(r)) {
      alerts.push({
        kind: "edge",
        position: p,
        title: `${pair} #${p.tokenId} is close to its range edge.`,
        detail: `Price ${price(r.priceCurrent)} against ${price(r.priceLower)} – ${price(r.priceUpper)}.`,
      });
    }
    const unclaimed = p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd;
    if (unclaimed >= UNCLAIMED_ALERT_USD) {
      alerts.push({
        kind: "unclaimed",
        position: p,
        title: `${usd(unclaimed)} unclaimed on ${pair} #${p.tokenId}.`,
        detail: "Fees and rewards waiting to be collected.",
      });
    }
    if (p.metrics.hasMissingPrices) {
      alerts.push({
        kind: "prices",
        position: p,
        title: `${pair} #${p.tokenId} has missing historical prices.`,
        detail: "Missing prices are counted as $0, so its figures may be understated.",
      });
    }
  }
  const order: Record<AlertKind, number> = { out: 0, edge: 1, unclaimed: 2, prices: 3 };
  return alerts.sort((a, b) => order[a.kind] - order[b.kind]);
}
