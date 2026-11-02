import { useOutletContext, useSearchParams } from 'react-router-dom';
import type { WalletResult } from '../api/client';
import { Bars, Kpi, PageHead } from '../components/ui';
import { alertsFor, incompleteReasons, isOpen, sum } from '../derive';
import { arrow, cls, pc, px, sg, usd } from '../format';

export default function Overview() {
  const w = useOutletContext<WalletResult>();
  const [, setParams] = useSearchParams();
  const open = w.positions.filter(isOpen);
  const value = sum(open, (p) => p.valueUsd + p.rewards.unclaimedUsd);
  const unclaimed = sum(open, (p) => p.rewards.unclaimedUsd);
  const { netPnlUsd: pnl, depositedUsd: dep, feesUsd: fees, ilUsd: il } = w.summary;
  const alerts = alertsFor(w.positions, px, usd);
  const reasons = incompleteReasons(w.positions);

  const byValue: [string, number][] = [...open].sort((a, b) => b.valueUsd - a.valueUsd).map((p) => [`${p.pair} #${p.tokenId}`, p.valueUsd]);
  const staked = sum(open.filter((p) => p.staked), (p) => p.valueUsd);
  const byCustody: [string, number][] = [['Unstaked', sum(open, (p) => p.valueUsd) - staked], ['Staked in gauges', staked]];
  byCustody.sort((a, b) => b[1] - a[1]);
  const aero = sum(open.filter((p) => p.staked), (p) => p.rewards.unclaimedUsd);
  const tradingFees = unclaimed - aero;
  const totalValue = sum(open, (p) => p.valueUsd);

  return (
    <>
      <PageHead title="Overview" sub="Everything you provide liquidity to on Aerodrome (Base)." />
      {reasons.length > 0 && (
        <div className="banner"><b>PnL is incomplete.</b> {reasons.join(' ')} Totals below are not yet trustworthy.</div>
      )}
      <div className="grid g4">
        <Kpi label="Portfolio value" value={usd(value)} sub={`${w.summary.openCount} open positions, incl. unclaimed`} />
        <Kpi label="Total PnL (all time)" value={sg(pnl)} tone={cls(pnl)} subTone={cls(pnl)}
          sub={`${arrow(pnl)} ${pc(dep > 0 ? (pnl / dep) * 100 : 0)} on ${usd(dep)} deposited`} />
        <Kpi label="Rewards earned" value={usd(fees)} sub={`${usd(unclaimed)} unclaimed`} />
        <Kpi label="Impermanent loss" value={sg(il)} tone={cls(il)} sub="Pool value vs holding" />
      </div>
      <div className="grid g-main">
        <div className="card">
          <div className="card-head"><h2>Portfolio history</h2></div>
          <p className="empty">History needs daily snapshots, which come with the database step. Not built yet.</p>
        </div>
        <div className="card">
          <div className="card-head"><h2>Needs attention</h2><span className="muted">{alerts.length}</span></div>
          {alerts.length === 0 && <p className="muted">Nothing needs attention.</p>}
          {alerts.map((a, i) => (
            <div className="alert" key={i}>
              <span className="ic" style={{ color: `var(--${a.tone})` }}>{a.icon}</span>
              <div>
                <div>{a.title}</div>
                <div className="note">{a.detail} <button onClick={() => setParams({ p: a.p.id })}>View position</button></div>
              </div>
            </div>
          ))}
        </div>
      </div>
      <div className="grid g2">
        <div className="card"><div className="card-head"><h2>Allocation by position</h2></div><Bars rows={byValue} total={totalValue} /></div>
        <div className="card">
          <div className="card-head"><h2>Staked vs unstaked</h2></div>
          <Bars rows={byCustody} total={totalValue} />
          <p className="note" style={{ margin: '14px 0 0' }}>
            Unclaimed: {usd(tradingFees)} trading fees (unstaked) and {usd(aero)} AERO emissions (staked). Staked positions earn AERO, not trading fees.
          </p>
        </div>
      </div>
    </>
  );
}
