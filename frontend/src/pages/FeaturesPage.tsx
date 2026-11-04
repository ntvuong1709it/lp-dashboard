type Phase = "MVP" | "Next" | "Later";

const phaseClass: Record<Phase, string> = {
  MVP: "bg-emerald-400/15 text-emerald-400",
  Next: "bg-sky-400/15 text-sky-400",
  Later: "bg-slate-800 text-slate-400",
};

const features: { phase: Phase; title: string; body: string; data: string; done?: boolean }[] = [
  { phase: "MVP", title: "Read-only wallet lookup", body: "Paste any address to see its positions. No wallet connection or signing, so nothing to approve.", data: "Wallet address", done: true },
  { phase: "MVP", title: "Positions across chains", body: "Uniswap v3 and Aerodrome Slipstream on Base and Ethereum, with more chains and protocols planned.", data: "On-chain reads", done: true },
  { phase: "MVP", title: "Range status and edge warnings", body: "In range, near edge, out of range, with the current price drawn against your range.", data: "Pool price", done: true },
  { phase: "MVP", title: "Value, fees, and P/L per position", body: "Deposit cost basis, current value, collected and unclaimed fees, gas.", data: "On-chain reads, price API", done: true },
  { phase: "MVP", title: "P/L breakdown", body: "Splits return into token price change, impermanent loss, fees and gas.", data: "Derived", done: true },
  { phase: "Next", title: "Historical value chart", body: "Portfolio value over time, built from hourly snapshots.", data: "Snapshots", done: true },
  { phase: "Next", title: "Compare against holding", body: "See whether providing liquidity beat holding the same tokens.", data: "Derived", done: true },
  { phase: "Next", title: "Out-of-range alerts", body: "Telegram message when a position leaves or returns to its range (built). Fee thresholds, Discord, email and browser alerts are planned.", data: "Scheduled checks", done: true },
  { phase: "Later", title: "Pool explorer and APR estimates", body: "Browse pools by fee APR, volume and TVL before you commit capital.", data: "Pool analytics API" },
  { phase: "Later", title: "Rebalance simulator", body: "Test a new range against past price action and see projected fees.", data: "Historical swaps" },
  { phase: "Later", title: "Multiple wallets", body: "Combine several addresses into one portfolio with per-wallet filters.", data: "Wallet addresses", done: true },
];

export function FeaturesPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Features</h1>
        <p className="text-sm text-slate-400">
          What this dashboard does, and what is planned, in build order.
        </p>
      </div>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {features.map((f) => (
          <div
            key={f.title}
            className="rounded-lg border border-slate-800 bg-slate-900 p-3"
          >
            <h3 className="flex items-start justify-between gap-2 text-sm font-semibold">
              <span>
                {f.done && (
                  <span title="Implemented" className="mr-1.5 text-emerald-400">
                    ✓
                  </span>
                )}
                {f.title}
              </span>
              <span
                className={`whitespace-nowrap rounded px-1.5 py-0.5 text-[11px] font-semibold ${phaseClass[f.phase]}`}
              >
                {f.phase}
              </span>
            </h3>
            <p className="mt-1 text-sm text-slate-400">{f.body}</p>
            <p className="mt-2 text-xs text-slate-500">Data: {f.data}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
