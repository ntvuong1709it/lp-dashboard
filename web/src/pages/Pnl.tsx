import { useOutletContext, useSearchParams } from 'react-router-dom';
import type { WalletResult } from '../api/client';
import { Bars, Kpi, PageHead, Waterfall } from '../components/ui';
import { incompleteReasons, sum } from '../derive';
import { cls, sg } from '../format';

export default function Pnl() {
  const w = useOutletContext<WalletResult>();
  const [, setParams] = useSearchParams();
  const ps = w.positions;
  const steps = [
    { label: 'Token price change', v: sum(ps, (p) => p.pnl.priceEffectUsd) },
    { label: 'Impermanent loss', v: sum(ps, (p) => p.pnl.ilUsd) },
    { label: 'Fees and AERO emissions', v: sum(ps, (p) => p.pnl.feesUsd) },
    { label: 'Gas spent', v: -sum(ps, (p) => p.pnl.gasUsd ?? 0) },
  ];
  const total = w.summary.netPnlUsd;
  const realized = sum(ps.filter((p) => p.state === 'closed'), (p) => p.pnl.netUsd);
  const lpVsHold = steps[1].v + steps[2].v + steps[3].v;
  const byPnl = [...ps].sort((a, b) => b.pnl.netUsd - a.pnl.netUsd);
  const reasons = incompleteReasons(ps);

  return (
    <>
      <PageHead title="Profit & loss" sub="Where your return comes from, and which positions drive it." />
      {reasons.length > 0 && <div className="banner"><b>PnL is incomplete.</b> {reasons.join(' ')}</div>}
      <div className="grid g4">
        <Kpi label="Net PnL" value={sg(total)} tone={cls(total)} sub="All positions" />
        <Kpi label="Unrealized" value={sg(total - realized)} tone={cls(total - realized)} sub="Open positions" />
        <Kpi label="Realized" value={sg(realized)} tone={cls(realized)} sub="Closed positions" />
        <Kpi label="LP vs just holding" value={sg(lpVsHold)} tone={cls(lpVsHold)} sub="IL + rewards − gas" />
      </div>
      <div className="grid g2">
        <div className="card">
          <div className="card-head"><h2>PnL breakdown</h2></div>
          <Waterfall steps={steps} total={total} />
          <p className="note" style={{ margin: '14px 0 0' }}>
            Token price change is what the deposited tokens gained or lost if held. Impermanent loss is the gap between the pool position and holding.
          </p>
        </div>
        <div className="card">
          <div className="card-head"><h2>PnL by position</h2></div>
          <Bars
            rows={byPnl.map((p) => [`${p.pair} #${p.tokenId}`, p.pnl.netUsd])}
            total={sum(ps, (p) => Math.abs(p.pnl.netUsd))}
            color={(v) => `var(${v >= 0 ? '--good' : '--bad'})`} />
          <p className="note" style={{ margin: '14px 0 0' }}>
            {byPnl.map((p) => <button key={p.id} className="btn" style={{ marginRight: 6 }} onClick={() => setParams({ p: p.id })}>#{p.tokenId}</button>)}
          </p>
        </div>
      </div>
      <div className="card">
        <h2>How PnL is calculated</h2>
        <p className="muted" style={{ margin: '6px 0 0', maxWidth: '70ch' }}>
          Net PnL = current value + collected + unclaimed rewards − deposits − gas. Deposits and withdrawals are valued in USD at the time they happened.
          Monthly charts and the ETH-denominated basis come with later steps.
        </p>
      </div>
    </>
  );
}
