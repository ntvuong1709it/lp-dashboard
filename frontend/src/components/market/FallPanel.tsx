import type { MarketWatchDto } from "../../api/types";
import { useFallImpact } from "../../lib/stress";
import { useOwner } from "../../lib/owner";
import { Panel, Segmented } from "../ui";
import { LoanFall, NoLoan } from "./LoanStress";
import { PositionsFall } from "./PositionsFall";

export const FALL_OPTIONS = [10, 20, 30].map((n) => ({ label: `−${n}%`, value: n }));

/**
 * "If prices fall": one control that drives the loan and every position. The loan comes from the server's stress table; the
 * positions are worked out here from their ranges, because a fall that stops a position earning is the part nothing else shows.
 */
export function FallPanel({
  data,
  fall,
  onFall,
}: {
  data: MarketWatchDto;
  fall: number;
  onFall: (n: number) => void;
}) {
  const { owner } = useOwner();
  const impact = useFallImpact(owner, fall);

  const lead = impact.loading
    ? ""
    : impact.stops === 0
      ? "Every position keeps earning."
      : `${impact.stops} of ${impact.total} ${impact.total === 1 ? "position stops" : "positions stop"} earning.`;

  return (
    <Panel title="If prices fall" actions={<span className="text-xs text-faint">ETH, BTC and AERO all fall by the same amount</span>}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <Segmented label="How far prices fall" options={FALL_OPTIONS} value={fall} onChange={onFall} />
        <span className="font-display text-xl font-semibold tracking-tight">{lead}</span>
      </div>
      {impact.alreadyOut > 0 && (
        <p className="mt-1 text-xs text-muted">
          {impact.alreadyOut} {impact.alreadyOut === 1 ? "position is" : "positions are"} already out of range today.
        </p>
      )}

      <div className="mt-5 grid gap-6 md:grid-cols-[minmax(0,1fr)_minmax(0,1.25fr)]">
        {data.loan ? <LoanFall stress={data.loan.stress} fallPercent={fall} /> : <NoLoan />}
        <PositionsFall impact={impact} />
      </div>

      <p className="mt-5 max-w-prose text-xs text-faint">
        A position that falls below its range holds only the falling token and earns no fees until the price comes back. Collateral is
        assumed to fall one for one with price, the worst case: LP collateral usually falls less.
      </p>
    </Panel>
  );
}
