import type { ClaimHealthStats } from "../../lib/earnings";
import { formatAmount, formatUsd } from "../../lib/format";
import { Panel } from "../ui";

function Mini({ label, value, sub }: { label: string; value: string; sub: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <div className="text-xs text-muted">{label}</div>
      <div className="text-xl font-medium num">{value}</div>
      <div className="truncate text-xs text-faint">{sub}</div>
    </div>
  );
}

/** Earned vs claimed vs waiting, and what the claims cost in gas. Claim details live in the log below. */
export function ClaimHealth({ stats }: { stats: ClaimHealthStats }) {
  const split = stats.claimed + stats.waiting;
  const claimedShare = split > 0 ? (stats.claimed / split) * 100 : 0;
  const gasShare = stats.claimed > 0 ? (stats.gas / stats.claimed) * 100 : null;
  // Claimed = earned + (waiting at the start of the window) - (waiting now), so this is what was waiting 30 days ago.
  const carried = stats.claimed - stats.earned + stats.waiting;
  const differs = Math.abs(stats.claimed - stats.earned) > Math.max(1, stats.earned * 0.02);
  const note = !differs
    ? null
    : carried > 0
      ? `Claimed is above earned because about ${formatUsd(carried)} had already built up before this 30-day window and was claimed inside it. Earned counts money by the day it accrued, claimed by the day you collected it.`
      : `Earned is above claimed plus waiting by about ${formatUsd(-carried)}. That is usually a price difference (claims use the price at claim time, earned uses the price at accrual) or a claim that is missing from the list.`;

  return (
    <Panel title="Claims, only when they matter · last 30 days">
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Mini label="Earned" value={formatUsd(stats.earned)} sub="accrued, claimed or not" />
        <Mini label="Claimed" value={formatUsd(stats.claimed)} sub={`${stats.claimCount} claims`} />
        <Mini
          label="Waiting to claim"
          value={formatUsd(stats.waiting)}
          sub={
            stats.waitingByToken.length
              ? stats.waitingByToken.map((t) => `${formatAmount(t.amount)} ${t.symbol}`).join(" · ")
              : "now, across open positions"
          }
        />
        <Mini
          label="Gas on claims"
          value={formatUsd(stats.gas)}
          sub={gasShare != null ? `${gasShare < 0.1 ? "<0.1" : gasShare.toFixed(1)}% of claimed` : "no claims"}
        />
      </div>
      {split > 0 && (
        <>
          <div className="mt-4 flex h-2.5 overflow-hidden rounded-full bg-raised">
            <div className="bg-up-fill" style={{ width: `${claimedShare}%` }} />
            <div className="bg-warn-fill" style={{ width: `${100 - claimedShare}%` }} />
          </div>
          <div className="mt-1.5 flex gap-4 text-xs text-muted">
            <span>
              <span className="mr-1.5 inline-block h-2 w-2 rounded-sm bg-up-fill" />
              Claimed
            </span>
            <span>
              <span className="mr-1.5 inline-block h-2 w-2 rounded-sm bg-warn-fill" />
              Still unclaimed
            </span>
          </div>
        </>
      )}
      {note && <p className="mt-3 text-xs text-faint">{note}</p>}
    </Panel>
  );
}
