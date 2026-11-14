import { usePortfolioInsights } from "../api/client";
import type {
  AssetClass,
  RewardTokenSummaryDto,
  TokenExposureDto,
} from "../api/types";
import {
  formatAmount,
  formatPercent,
  formatPrice,
  formatUsd,
  pnlClass,
} from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { BarRow, Empty, ErrorText, Panel, Skeleton } from "./ui";

const classStyle: Record<AssetClass, { label: string; bar: string }> = {
  Stablecoin: { label: "Stablecoins", bar: "bg-up-fill" },
  Ether: { label: "ETH", bar: "bg-[var(--wallet-1)]" },
  Bitcoin: { label: "BTC", bar: "bg-warn-fill" },
  Other: { label: "Other", bar: "bg-muted" },
};

const pct = (part: number, total: number) =>
  total > 0 ? `${((part / total) * 100).toFixed(0)}%` : "0%";

/**
 * What the open positions actually hold, token by token. A concentrated position ends up in whichever token is
 * losing, so this is the real directional risk of the whole portfolio, not what the pair names suggest.
 */
export function ExposurePanel() {
  useHideBalances();
  const { owner } = useOwner();
  const { data, error, isLoading } = usePortfolioInsights(owner);
  const e = data?.exposure;

  const stableShare =
    e && e.totalUsd > 0
      ? (e.classes.find((c) => c.assetClass === "Stablecoin")?.totalUsd ?? 0) /
        e.totalUsd
      : null;
  const max = Math.max(...(e?.tokens ?? []).map((t) => t.totalUsd), 0);

  return (
    <Panel
      title="Token exposure"
      actions={
        stableShare != null && (
          <span className="text-xs text-muted">
            {((1 - stableShare) * 100).toFixed(0)}% price-exposed ·{" "}
            {(stableShare * 100).toFixed(0)}% stable
          </span>
        )
      }
    >
      {error ? (
        <ErrorText error={error} />
      ) : isLoading ? (
        <Skeleton className="h-40 w-full" />
      ) : !e || !e.tokens.length ? (
        <Empty>No open positions.</Empty>
      ) : (
        <div className="space-y-4">
          <div>
            <div
              className="flex h-3 overflow-hidden rounded-full bg-raised"
              role="img"
              aria-label="Exposure by asset class"
            >
              {e.classes.map((c) => (
                <div
                  key={c.assetClass}
                  className={classStyle[c.assetClass].bar}
                  style={{ width: `${(c.totalUsd / e.totalUsd) * 100}%` }}
                  title={`${classStyle[c.assetClass].label} ${formatUsd(c.totalUsd)}`}
                />
              ))}
            </div>
            <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
              {e.classes.map((c) => (
                <span key={c.assetClass}>
                  <span
                    className={`mr-1.5 inline-block h-2 w-2 rounded-sm ${classStyle[c.assetClass].bar}`}
                  />
                  {classStyle[c.assetClass].label} {pct(c.totalUsd, e.totalUsd)}
                  <span className="ml-1 text-faint num">
                    {formatUsd(c.totalUsd)}
                  </span>
                </span>
              ))}
            </div>
          </div>

          <div className="space-y-2.5">
            {e.tokens.map((t: TokenExposureDto) => (
              <BarRow
                key={t.symbol}
                label={t.symbol}
                fraction={max > 0 ? t.totalUsd / max : 0}
                barClass={classStyle[t.assetClass].bar}
                value={formatUsd(t.totalUsd)}
                sub={pct(t.totalUsd, e.totalUsd)}
              />
            ))}
          </div>

          <p className="text-xs text-faint">
            Amounts held inside open positions plus their unclaimed fees and
            rewards, at today&apos;s prices. A position that is out of range sits
            entirely in one token.
            {e.hasMissingPrices &&
              " Some holdings have no price and are not counted."}
          </p>
        </div>
      )}
    </Panel>
  );
}

function changeNote(r: RewardTokenSummaryDto): string {
  if (r.changeSinceClaimUsd == null) return "No current price";
  return r.changeSinceClaimUsd >= 0
    ? "Holding beat selling at claim"
    : "Selling at claim would have been better";
}

/**
 * Per reward token: what it was worth when claimed versus today. The app cannot see whether claimed tokens were
 * sold, so this shows what holding them has done, which is also what selling at claim would have saved or cost.
 */
export function RewardTokensPanel() {
  useHideBalances();
  const { owner } = useOwner();
  const { data, error, isLoading } = usePortfolioInsights(owner);
  const rewards = data?.rewards ?? [];

  return (
    <Panel title="Reward tokens: claimed vs today">
      {error ? (
        <ErrorText error={error} />
      ) : isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : !rewards.length ? (
        <Empty>No reward claims yet.</Empty>
      ) : (
        <div className="space-y-4">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-xs uppercase tracking-[.05em] text-muted">
                <tr>
                  <th className="pb-2 pr-3 font-normal">Token</th>
                  <th className="pb-2 pr-3 text-right font-normal">Claimed</th>
                  <th className="pb-2 pr-3 text-right font-normal">
                    Avg price at claim
                  </th>
                  <th className="pb-2 pr-3 text-right font-normal">Price now</th>
                  <th className="pb-2 pr-3 text-right font-normal">
                    Worth then
                  </th>
                  <th className="pb-2 pr-3 text-right font-normal">
                    Worth now
                  </th>
                  <th className="pb-2 text-right font-normal">Change</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rewards.map((r) => (
                  <tr key={r.address ?? r.symbol}>
                    <td className="whitespace-nowrap py-2 pr-3">
                      <div className="font-medium">
                        {r.symbol}
                        {r.address &&
                          rewards.filter((o) => o.symbol === r.symbol).length > 1 && (
                            <span
                              className="ml-1.5 text-xs font-normal text-faint"
                              title={r.address}
                            >
                              {r.address.slice(0, 6)}…{r.address.slice(-4)}
                            </span>
                          )}
                      </div>
                      {r.currentPriceUsd == null && (
                        <div
                          className="whitespace-nowrap text-xs text-faint"
                          title="No open position pays this token any more, so there is no current price to compare with."
                        >
                          no current price
                        </div>
                      )}
                      <div className="text-xs text-faint">
                        {r.claimCount} claim{r.claimCount === 1 ? "" : "s"}
                        {r.pendingAmount > 0 &&
                          ` · ${formatAmount(r.pendingAmount)} pending`}
                      </div>
                    </td>
                    <td className="whitespace-nowrap py-2 pr-3 text-right num">
                      {formatAmount(r.claimedAmount)}
                    </td>
                    <td className="whitespace-nowrap py-2 pr-3 text-right num">
                      {r.averageClaimPriceUsd != null
                        ? `$${formatPrice(r.averageClaimPriceUsd)}`
                        : "—"}
                      {r.hasEstimatedPrices && (
                        <span
                          className="ml-1 text-warn"
                          title="Some claims had no price on the day and use the next known price."
                        >
                          ~
                        </span>
                      )}
                    </td>
                    <td className="whitespace-nowrap py-2 pr-3 text-right num">
                      {r.currentPriceUsd != null
                        ? `$${formatPrice(r.currentPriceUsd)}`
                        : "—"}
                    </td>
                    <td className="whitespace-nowrap py-2 pr-3 text-right num">
                      {formatUsd(r.claimedUsd)}
                    </td>
                    <td className="whitespace-nowrap py-2 pr-3 text-right num">
                      {formatUsd(r.claimedValueNowUsd)}
                    </td>
                    <td
                      className={`py-2 text-right num ${pnlClass(r.changeSinceClaimUsd ?? 0)}`}
                      title={changeNote(r)}
                    >
                      {formatUsd(r.changeSinceClaimUsd, true)}
                      <span className="ml-1 text-xs">
                        {formatPercent(r.changeSinceClaimPercent, true)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {rewards.some((r) => r.hasMissingPrices) && (
            <p className="text-xs text-warn">
              Some claims have no price at all and are not valued.
            </p>
          )}
          <p className="text-xs text-faint">
            Assumes claimed tokens were kept. A negative change is what
            selling right after each claim would have saved; a positive one is
            what holding earned. Pending rewards are on the Unclaimed card.
          </p>
        </div>
      )}
    </Panel>
  );
}
