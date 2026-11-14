import { useMemo } from "react";
import type { DailyEarningsDto } from "../../api/types";
import { tokenRows, type TokenAmount } from "../../lib/earnings";
import { formatAmount, formatUsd } from "../../lib/format";
import { Panel } from "../ui";

function Cell({ a }: { a: TokenAmount }) {
  if (a.amount <= 0 && a.usd <= 0) return <span className="text-faint">–</span>;
  return (
    <>
      {formatAmount(a.amount)} <span className="text-faint">{formatUsd(a.usd)}</span>
    </>
  );
}

/** What was earned in each token: the reward token and the two tokens swap fees are paid in. */
export function TokenBreakdown({ days }: { days: DailyEarningsDto[] }) {
  const rows = useMemo(() => tokenRows(days), [days]);
  if (!rows.length) return null;

  return (
    <Panel title="What you earned, by token">
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-left text-xs uppercase text-faint">
            <tr>
              <th className="py-1 pr-3">Token</th>
              <th className="py-1 px-3 text-right">Yesterday</th>
              <th className="py-1 px-3 text-right">Last 7 days</th>
              <th className="py-1 px-3 text-right">This month</th>
              <th className="py-1 pl-3">Share</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((r) => (
              <tr key={r.key}>
                <td className="whitespace-nowrap py-1.5 pr-3 font-medium">
                  {r.symbol}
                  <span className="ml-2 rounded border border-line-strong px-1 text-[11px] font-normal text-muted">
                    {r.kind === "reward" ? "reward" : "fee"}
                  </span>
                </td>
                <td className="whitespace-nowrap px-3 py-1.5 text-right num">
                  <Cell a={r.yesterday} />
                </td>
                <td className="whitespace-nowrap px-3 py-1.5 text-right num">
                  <Cell a={r.last7} />
                </td>
                <td className="whitespace-nowrap px-3 py-1.5 text-right num">
                  <Cell a={r.month} />
                </td>
                <td className="whitespace-nowrap py-1.5 pl-3">
                  <span className="num">{Math.round(r.share * 100)}%</span>
                  <span className="ml-2 inline-block h-1.5 w-16 overflow-hidden rounded bg-raised align-middle">
                    <span
                      className="block h-full"
                      style={{
                        width: `${r.share * 100}%`,
                        background: r.kind === "reward" ? "var(--chart-d)" : "var(--chart-a)",
                      }}
                    />
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-2 text-xs text-faint">
        Swap fees are paid in the pool's two tokens, rewards in the gauge token. USD uses the price at the time
        each amount accrued.
      </p>
    </Panel>
  );
}
