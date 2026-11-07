import { Fragment, useState } from "react";
import type { WeeklyClaimsDto } from "../../api/types";
import { formatAmount, formatDate, formatUsd, protocolLabels, txUrl } from "../../lib/format";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { CopyButton, ExternalLink } from "../ui";

/** Every claim, grouped by week and collapsed by default; the summary says whether anything looks off. */
export function ClaimLog({
  weeks,
  flags,
}: {
  weeks: WeeklyClaimsDto[];
  flags: Map<string, string>;
}) {
  const drawer = usePositionDrawer();
  const [expanded, setExpanded] = useState<string | null>(null);
  if (!weeks.length) return null;

  const count = weeks.reduce((n, w) => n + w.claimCount, 0);

  return (
    <details className="group rounded-lg border border-slate-800 bg-slate-900">
      <summary className="flex cursor-pointer list-none flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3 text-sm font-medium text-slate-300 [&::-webkit-details-marker]:hidden">
        <span className="text-slate-500 group-open:hidden">▸</span>
        <span className="hidden text-slate-500 group-open:inline">▾</span>
        Claim history
        {flags.size === 0 ? (
          <span className="rounded-full bg-emerald-500/15 px-2 text-[11px] font-semibold uppercase tracking-wide text-emerald-400">
            All normal
          </span>
        ) : (
          <span className="rounded-full bg-amber-500/15 px-2 text-[11px] font-semibold uppercase tracking-wide text-amber-300">
            {flags.size} to check
          </span>
        )}
        <span className="text-xs font-normal text-slate-500">{count} claims in total</span>
      </summary>

      <div className="overflow-x-auto px-4 pb-4">
        <table className="w-full text-sm">
          <thead className="text-left text-xs uppercase text-slate-500">
            <tr>
              <th className="py-1">Week of</th>
              <th className="py-1 text-right">Claims</th>
              <th className="py-1 text-right">Swap fees</th>
              <th className="py-1 text-right">Rewards</th>
              <th className="py-1 text-right">Total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {weeks.map((w) => (
              <Fragment key={w.weekStart}>
                <tr
                  className="cursor-pointer hover:bg-slate-800/50"
                  onClick={() => setExpanded(expanded === w.weekStart ? null : w.weekStart)}
                >
                  <td className="py-1.5">
                    {expanded === w.weekStart ? "▾" : "▸"} {w.weekStart}
                  </td>
                  <td className="py-1.5 text-right num">{w.claimCount}</td>
                  <td className="py-1.5 text-right num">{formatUsd(w.feesUsd)}</td>
                  <td className="py-1.5 text-right num">{formatUsd(w.rewardsUsd)}</td>
                  <td className="py-1.5 text-right num font-medium">{formatUsd(w.totalUsd)}</td>
                </tr>
                {expanded === w.weekStart &&
                  w.items.map((i) => (
                    <tr key={`${i.txHash}-${i.positionId}-${i.type}`} className="bg-slate-950/50 text-xs">
                      <td className="py-1 pl-6 text-slate-400">
                        {formatDate(i.timestamp)}{" "}
                        {/* Claims carry no chain yet; Base is the only synced chain. */}
                        <ExternalLink href={txUrl("Base", i.txHash)}>tx</ExternalLink>
                        <CopyButton text={i.txHash} title="Copy transaction hash" />
                        {flags.has(i.txHash) && (
                          <span className="ml-1 text-amber-400" title={flags.get(i.txHash)}>
                            ⚠
                          </span>
                        )}
                      </td>
                      <td className="py-1" colSpan={2}>
                        <button
                          type="button"
                          className="text-sky-400 hover:underline"
                          onClick={() => drawer.open(i.positionId)}
                        >
                          {i.pair} #{i.tokenId}
                        </button>{" "}
                        <span className="text-slate-500">{protocolLabels[i.protocol]}</span>
                      </td>
                      <td className="py-1 text-right num text-slate-300">
                        {i.type === "RewardClaim"
                          ? `${formatAmount(i.rewardAmount)} ${i.rewardTokenSymbol ?? ""}`
                          : `${formatAmount(i.fee0)} ${i.token0Symbol} + ${formatAmount(i.fee1)} ${i.token1Symbol}`}
                      </td>
                      <td className="py-1 text-right num">
                        {formatUsd(i.totalUsd)}
                        {i.gasUsd > 0 && <span className="ml-1 text-slate-500">gas {formatUsd(i.gasUsd)}</span>}
                        {i.hasMissingPrices && (
                          <span className="ml-1 text-amber-400" title="Price missing">
                            *
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}
