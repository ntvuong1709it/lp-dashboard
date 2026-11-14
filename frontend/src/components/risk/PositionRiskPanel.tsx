import { useState } from "react";
import { useTokenRisk } from "../../api/risk";
import type { PositionDto, RiskSummaryDto } from "../../api/types";
import { RiskBadge } from "./RiskBadge";
import { RiskReport } from "./RiskReport";
import { ErrorText, Panel, Skeleton } from "../ui";

/** One token of the position, looked up lazily once its section is opened (a recent check comes straight from the cache). */
function TokenSection({ position, address, symbol }: { position: PositionDto; address: string; symbol: string }) {
  const [open, setOpen] = useState(false);
  const query = useTokenRisk(position.chain, open ? address : null);
  return (
    <details className="rounded-md border border-line" onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 px-3 py-2 text-sm">
        <span className="font-medium">{symbol}</span>
        <span className="text-xs text-faint">{open ? "" : "Open for the full report"}</span>
      </summary>
      <div className="border-t border-line p-3">
        {query.data ? (
          <RiskReport report={query.data} compact />
        ) : query.error ? (
          <ErrorText error={query.error} />
        ) : (
          <div className="space-y-2">
            <Skeleton className="h-4 w-1/2" />
            <Skeleton className="h-10 w-full" />
          </div>
        )}
      </div>
    </details>
  );
}

/**
 * The position's risk: the cached verdict of its pool and tokens at a glance, and each token's full report on request.
 * Reading this never triggers a check by itself; opening a token does, when none is cached.
 */
export function PositionRiskPanel({ position }: { position: PositionDto }) {
  const summary: RiskSummaryDto | null = position.risk;
  const tokens = [position.token0, position.token1].filter((t): t is NonNullable<typeof t> => t != null);
  if (tokens.length === 0) return null;

  return (
    <Panel title="Risk" actions={<RiskBadge summary={summary} />}>
      <div className="space-y-3">
        {summary == null && (
          <p className="text-sm text-muted">
            Nothing was checked for these tokens yet. Open one below to run its check; the nightly job re-checks the tokens you
            hold.
          </p>
        )}
        {tokens.map((t) => (
          <TokenSection key={t.address} position={position} address={t.address} symbol={t.symbol} />
        ))}
        <p className="text-xs text-faint">No flags is not a guarantee of safety.</p>
      </div>
    </Panel>
  );
}
