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
import { useDailyEarnings, useWeeklyClaims } from "../api/client";
import { Empty, ErrorText, Panel, StatCard } from "../components/ui";
import {
  formatAmount,
  formatDate,
  formatUsd,
  protocolLabels,
} from "../lib/format";
import { useOwner } from "../lib/owner";

export function FeeClaimsPage() {
  return (
    <div className="space-y-10">
      <DailyEarnings />
      <WeeklyClaims />
    </div>
  );
}

function DailyEarnings() {
  const { owner } = useOwner();
  const { data, error, isLoading } = useDailyEarnings(owner);

  if (error) return <ErrorText error={error} />;
  if (isLoading) return <Empty>Loading…</Empty>;
  if (!data?.length) {
    return (
      <Empty>
        No earnings tracked yet. Daily earnings build up from hourly snapshots
        of open positions.
      </Empty>
    );
  }

  const chart = [...data].reverse();
  const last7 = data.slice(0, 7);
  const avg7 = last7.reduce((sum, d) => sum + d.totalUsd, 0) / last7.length;
  const total = data.reduce((sum, d) => sum + d.totalUsd, 0);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatCard
          label="Today"
          value={formatUsd(data[0].totalUsd)}
          hint="so far, claimed or not"
        />
        <StatCard label="Avg / day (last 7)" value={formatUsd(avg7)} />
        <StatCard
          label="Total tracked"
          value={formatUsd(total)}
          hint={`since ${data[data.length - 1].date}`}
        />
        <StatCard label="Est. / month" value={formatUsd(avg7 * 30)} />
      </div>

      <Panel title="Earned per day (incl. unclaimed, USD)">
        <div className="h-64">
          <ResponsiveContainer>
            <BarChart data={chart}>
              <CartesianGrid stroke="#1e293b" vertical={false} />
              <XAxis dataKey="date" stroke="#64748b" fontSize={11} />
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
              <Bar
                dataKey="feesUsd"
                name="Swap fees"
                stackId="a"
                fill="#34d399"
              />
              <Bar
                dataKey="rewardsUsd"
                name="Rewards"
                stackId="a"
                fill="#38bdf8"
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Panel>

      <Panel title="Daily earnings">
        <table className="w-full text-sm">
          <thead className="text-left text-xs uppercase text-slate-500">
            <tr>
              <th className="py-1">Day</th>
              <th className="py-1 text-right">Swap fees</th>
              <th className="py-1 text-right">Rewards</th>
              <th className="py-1 text-right">Total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {data.map((d) => (
              <tr key={d.date}>
                <td className="py-1.5">{d.date}</td>
                <td className="py-1.5 text-right num">
                  {formatUsd(d.feesUsd)}
                </td>
                <td className="py-1.5 text-right num">
                  {formatUsd(d.rewardsUsd)}
                </td>
                <td className="py-1.5 text-right num font-medium">
                  {formatUsd(d.totalUsd)}
                  {d.hasMissingPrices && (
                    <span className="ml-1 text-amber-400" title="Price missing">
                      *
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}

function WeeklyClaims() {
  const { owner } = useOwner();
  const { data, error, isLoading } = useWeeklyClaims(owner);
  const [expanded, setExpanded] = useState<string | null>(null);

  if (error) return <ErrorText error={error} />;
  if (isLoading) return <Empty>Loading…</Empty>;
  if (!data?.length) return <Empty>No fee claims yet.</Empty>;

  const chart = [...data].reverse().map((w) => ({
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
