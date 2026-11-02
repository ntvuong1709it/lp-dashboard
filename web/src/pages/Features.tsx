import { PageHead } from '../components/ui';

type Phase = 1 | 2 | 3;
const FEATURES: [Phase, string, string, string][] = [
  [1, 'Read-only wallet lookup', 'Paste any address to see its positions. No wallet connection or signing, so nothing to approve.', 'Wallet address'],
  [1, 'Positions across protocols', 'Aerodrome first, then Uniswap and Meteora.', 'Subgraphs, on-chain reads, Meteora API'],
  [1, 'Range status and edge warnings', 'In range, near edge, out of range, with the current price drawn against your range.', 'Pool price reads'],
  [1, 'Value, rewards, and PnL per position', 'Deposit cost basis, current value, collected and unclaimed fees or emissions, gas.', 'Chain logs, price API'],
  [1, 'PnL breakdown', 'Splits return into token price change, impermanent loss, fees and gas.', 'Derived'],
  [2, 'Historical value chart', 'Daily portfolio value and cumulative PnL from stored snapshots.', 'Database, historical prices'],
  [2, 'Compare against holding', 'See whether providing liquidity beat holding the same tokens, in USD or ETH.', 'Derived'],
  [2, 'Out-of-range alerts', 'Email or Telegram message when a position leaves its range.', 'Scheduled checks'],
  [2, 'CSV export for tax', 'Every add, remove and collect with USD value at the time.', 'Stored events'],
  [3, 'Pool explorer and APR estimates', 'Browse pools by fee APR, volume and TVL before you commit capital.', 'Pool analytics API'],
  [3, 'Rebalance simulator', 'Test a new range against past price action and see projected fees.', 'Historical swaps'],
  [3, 'Multiple wallets', 'Combine several addresses into one portfolio.', 'Wallet addresses'],
];
const NAME = { 1: 'MVP', 2: 'Next', 3: 'Later' } as const;

export default function Features() {
  return (
    <>
      <PageHead title="Features" sub="What this site does, in the order it is built. Build progress is tracked in STATUS.md in the repository." />
      <div className="spec-grid">
        {FEATURES.map(([phase, title, text, data]) => (
          <div className="spec" key={title}>
            <h3>{title}<span className={`ph ph${phase}`}>{NAME[phase]}</span></h3>
            <p>{text}</p>
            <p className="note" style={{ marginTop: 8 }}>Data: {data}</p>
          </div>
        ))}
      </div>
    </>
  );
}
