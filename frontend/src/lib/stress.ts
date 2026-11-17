import { useMemo } from "react";
import { usePortfolioInsights, usePositions } from "../api/client";
import type { AssetClass, PositionDto } from "../api/types";
import { pairLabel } from "./attention";
import { invertRange } from "./format";

/** How close above its lower edge a position can land and still be called "at the edge", in percent of the price. */
const EDGE_MARGIN_PERCENT = 3;

export type FallStatus =
  /** Earning after the fall. */
  | "ok"
  /** Still in range, but within a few percent of the edge. */
  | "edge"
  /** Falls below its range and stops earning. */
  | "out"
  /** Already out of range today. */
  | "already-out"
  /** Both tokens are stable, or both move with price, so the pair does not move against its range. */
  | "unaffected"
  /** No range or no current price to work from. */
  | "no-range";

export interface PositionFall {
  position: PositionDto;
  pair: string;
  status: FallStatus;
  /** Range and price in stable-per-volatile terms; null unless the status depends on them. */
  view: { lower: number; upper: number; current: number; after: number; unit: string } | null;
  /** The fall in percent that takes the price to the lower edge; null when it does not apply. */
  fallToEdgePercent: number | null;
}

const STABLE_SYMBOL = /(^|[^a-z])(usd|dai|frax|gho|lusd|eur)|usd$/i;

/** Whether a token holds its dollar value: the app's own classification when it has one, else a guess from the name. */
export function isStableToken(symbol: string | undefined, classes: ReadonlyMap<string, AssetClass>): boolean {
  if (!symbol) return false;
  const known = classes.get(symbol);
  return known != null ? known === "Stablecoin" : STABLE_SYMBOL.test(symbol);
}

/**
 * What a fall in everything that is not a stablecoin does to one position. When one token is stable and the other is not,
 * the price in stable terms falls by the same percent, and the position stops earning once it passes its lower edge.
 */
export function positionFall(p: PositionDto, fallPercent: number, classes: ReadonlyMap<string, AssetClass>): PositionFall {
  const pair = pairLabel(p);
  const base = { position: p, pair, view: null, fallToEdgePercent: null };
  const r = p.range;
  if (!r || r.priceCurrent == null || !(r.priceCurrent > 0)) return { ...base, status: "no-range" };

  const stable0 = isStableToken(p.token0?.symbol, classes);
  const stable1 = isStableToken(p.token1?.symbol, classes);
  if (stable0 === stable1) return { ...base, status: "unaffected" };
  if (r.inRange === false) return { ...base, status: "already-out" };

  // Show prices as stable per volatile token ("USDC per WETH"), so a fall always moves the price down.
  const range = stable0 ? invertRange(r) : r;
  const stable = stable0 ? p.token0?.symbol : p.token1?.symbol;
  const volatile = stable0 ? p.token1?.symbol : p.token0?.symbol;
  const current = range.priceCurrent;
  if (current == null || !(current > 0) || !(range.priceLower > 0)) return { ...base, status: "no-range" };

  const after = current * (1 - fallPercent / 100);
  const status: FallStatus =
    after < range.priceLower ? "out" : ((after - range.priceLower) / current) * 100 < EDGE_MARGIN_PERCENT ? "edge" : "ok";
  return {
    ...base,
    status,
    view: { lower: range.priceLower, upper: range.priceUpper, current, after, unit: `${stable} per ${volatile}` },
    fallToEdgePercent: ((current - range.priceLower) / current) * 100,
  };
}

export interface FallImpact {
  loading: boolean;
  total: number;
  /** Positions the fall affects, closest to the edge first. */
  affected: PositionFall[];
  unaffected: PositionFall[];
  /** Positions that stop earning because of the fall. */
  stops: number;
  alreadyOut: number;
}

/** Every open position tested against a fall of everything that is not a stablecoin. */
export function useFallImpact(owner: string | null, fallPercent: number): FallImpact {
  const open = usePositions(owner, "Open");
  const insights = usePortfolioInsights(owner);
  const classes = useMemo(
    () => new Map((insights.data?.exposure.tokens ?? []).map((t) => [t.symbol, t.assetClass] as const)),
    [insights.data],
  );
  return useMemo(() => {
    const all = (open.data ?? []).map((p) => positionFall(p, fallPercent, classes));
    const affected = all
      .filter((f) => f.status !== "unaffected" && f.status !== "no-range")
      .sort((a, b) => (a.fallToEdgePercent ?? -1) - (b.fallToEdgePercent ?? -1));
    return {
      loading: open.isLoading || insights.isLoading,
      total: all.length,
      affected,
      unaffected: all.filter((f) => f.status === "unaffected" || f.status === "no-range"),
      stops: all.filter((f) => f.status === "out").length,
      alreadyOut: all.filter((f) => f.status === "already-out").length,
    };
  }, [open.data, open.isLoading, insights.isLoading, classes, fallPercent]);
}
