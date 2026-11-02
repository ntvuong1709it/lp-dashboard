import type { Position, PositionEvent, WalletResult } from './api/client';

const NEAR_EDGE = 0.08;

/** Fraction of the range width the price sits from the nearest edge, only for in-range positions. */
export const isNearEdge = (p: Position) => {
  if (p.state !== 'in') return false;
  const pos = (p.priceCurrent - p.priceLower) / (p.priceUpper - p.priceLower);
  return pos < NEAR_EDGE || pos > 1 - NEAR_EDGE;
};

export const pillOf = (p: Position): { cls: string; text: string } =>
  p.state === 'closed' ? { cls: 'closed', text: 'Closed' }
  : p.state === 'out' ? { cls: 'out', text: 'Out of range' }
  : isNearEdge(p) ? { cls: 'edge', text: 'Near edge' }
  : { cls: 'in', text: 'In range' };

export const rewardLabel = (p: Position) => (p.rewards.kind === 'aeroEmissions' ? 'AERO emissions' : 'Trading fees');

export const ageDays = (p: Position) => {
  if (p.openedAt == null) return 0;
  const end = p.closedAt ?? Date.now() / 1000;
  return Math.max(0, Math.round((end - p.openedAt) / 86400));
};

export const isOpen = (p: Position) => p.state !== 'closed';

export const sum = (ps: Position[], f: (p: Position) => number) => ps.reduce((s, p) => s + f(p), 0);

export interface Alert { p: Position; tone: 'bad' | 'warn' | 'accent'; icon: string; title: string; detail: string }

export function alertsFor(positions: Position[], fmt: (n: number) => string, usd: (n: number) => string): Alert[] {
  const out: Alert[] = [];
  for (const p of positions.filter(isOpen)) {
    const range = `${fmt(p.priceLower)} – ${fmt(p.priceUpper)}`;
    if (p.state === 'out')
      out.push({ p, tone: 'bad', icon: '!', title: `${p.pair} is out of range and earning nothing.`,
        detail: `Price ${fmt(p.priceCurrent)} is ${p.priceCurrent > p.priceUpper ? 'above' : 'below'} your ${range} range.` });
    else if (isNearEdge(p))
      out.push({ p, tone: 'warn', icon: '~', title: `${p.pair} is close to its range edge.`, detail: `Price ${fmt(p.priceCurrent)} against ${range}.` });
    if (p.rewards.unclaimedUsd >= 200)
      out.push({ p, tone: 'accent', icon: '$', title: `${usd(p.rewards.unclaimedUsd)} in unclaimed ${p.rewards.kind === 'aeroEmissions' ? 'AERO' : 'fees'} on ${p.pair}.`,
        detail: p.staked ? 'Staked in the gauge, claim from the Aerodrome UI.' : 'Collect from the Aerodrome UI.' });
  }
  return out;
}

export interface FlatEvent extends PositionEvent { p: Position }

export const flattenEvents = (w: WalletResult): FlatEvent[] =>
  w.positions.flatMap((p) => p.events.map((e) => ({ ...e, p }))).sort((a, b) => b.timestamp - a.timestamp);

export const incompleteReasons = (positions: Position[]) => [...new Set(positions.flatMap((p) => p.incomplete))];
