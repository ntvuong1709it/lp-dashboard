import { Fragment, useState } from "react";
import { Link } from "react-router-dom";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useWeeklyClaims } from "../api/client";
import { Empty, ErrorText, Panel, StatCard } from "../components/ui";
import {
  formatAmount,
  formatDate,
  formatUsd,
  protocolLabels,
} from "../lib/format";
import { useOwner } from "../lib/owner";

export function FeeClaimsPage() {
  const { owner } = useOwner();
  const { data, error, isLoading } = useWeeklyClaims(owner);
  const [expanded, setExpanded] = useState<string | null>(null);

  if (error) return <ErrorText error={error} />;
  if (isLoading) return <Empty>Loading…</Empty>;
  if (!data?.length) return <Empty>No fee claims yet.</Empty>;

  const chart = [...data]
    .reverse()
    .map((w) => ({
      week: w.weekStart,
      fees: w.feesUsd,
      rewards: w.rewardsUsd,
    }));
  const total = data.reduce((sum, w) => sum + w.totalUsd, 0);
  const recent = data.slice(0, 4);
  const avg4w = recent.reduce((sum, w) => sum + w.totalUsd, 0) / recent.length;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatCard
          label="Total claimed"
          value={formatUsd(total)}
          hint={`${data.length} weeks with claims`}
        />
        <StatCard
          label="This week"
          value={formatUsd(data[0].totalUsd)}
          hint={`week of ${data[0].weekStart}`}
        />
        <StatCard label="Avg (last 4 claim weeks)" value={formatUsd(avg4w)} />
        <StatCard
          label="Claims"
          value={data.reduce((n, w) => n + w.claimCount, 0)}
        />
      </div>

      <Panel title="Claimed per week (USD at claim time)">
        <div className="h-64">
          <ResponsiveContainer>
            <BarChart data={chart}>
              <CartesianGrid stroke="#1e293b" vertical={false} />
              <XAxis dataKey="week" stroke="#64748b" fontSize={11} />
              <YAxis
                stroke="#64748b"
                fontSize={11}
                tickFormatter={(v) => formatUsd(v)}
                width={80}
              />
              <Tooltip
                contentStyle={{
                  background: "#0f172a",
                  border: "1px solid #334155",
                }}
                formatter={(v) => formatUsd(v as number)}
              />
              <Legend />
              <Bar dataKey="fees" name="Swap fees" stackId="a" fill="#34d399" />
              <Bar
                dataKey="rewards"
                name="Rewards"
                stackId="a"
                fill="#38bdf8"
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Panel>

      <Panel title="Weekly claims">
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
            {data.map((w) => (
              <Fragment key={w.weekStart}>
                <tr
                  className="cursor-pointer hover:bg-slate-800/50"
                  onClick={() =>
                    setExpanded(expanded === w.weekStart ? null : w.weekStart)
                  }
                >
                  <td className="py-1.5">
                    {expanded === w.weekStart ? "▾" : "▸"} {w.weekStart}
                  </td>
                  <td className="py-1.5 text-right num">{w.claimCount}</td>
                  <td className="py-1.5 text-right num">
                    {formatUsd(w.feesUsd)}
                  </td>
                  <td className="py-1.5 text-right num">
                    {formatUsd(w.rewardsUsd)}
                  </td>
                  <td className="py-1.5 text-right num font-medium">
                    {formatUsd(w.totalUsd)}
                  </td>
                </tr>
                {expanded === w.weekStart &&
                  w.items.map((i) => (
                    <tr
                      key={`${i.txHash}-${i.positionId}-${i.type}`}
                      className="bg-slate-950/50 text-xs"
                    >
                      <td className="py-1 pl-6 text-slate-400">
                        {formatDate(i.timestamp)}
                      </td>
                      <td className="py-1" colSpan={2}>
                        <Link
                          className="text-sky-400 hover:underline"
                          to={`/positions/${i.positionId}`}
                        >
                          {i.pair} #{i.tokenId}
                        </Link>{" "}
                        <span className="text-slate-500">
                          {protocolLabels[i.protocol]}
                        </span>
                      </td>
                      <td className="py-1 text-right num text-slate-300">
                        {i.type === "RewardClaim"
                          ? `${formatAmount(i.rewardAmount)} ${i.rewardTokenSymbol ?? ""}`
                          : `${formatAmount(i.fee0)} ${i.token0Symbol} + ${formatAmount(i.fee1)} ${i.token1Symbol}`}
                      </td>
                      <td className="py-1 text-right num">
                        {formatUsd(i.totalUsd)}
                        {i.hasMissingPrices && (
                          <span
                            className="ml-1 text-amber-400"
                            title="Price missing"
                          >
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
      </Panel>
    </div>
  );
}
