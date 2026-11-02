import { useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router-dom';
import type { Position, WalletResult } from '../api/client';
import { Pill, PageHead, RangeBar, Seg } from '../components/ui';
import { ageDays, rewardLabel } from '../derive';
import { arrow, cls, pc, px, sg, usd } from '../format';

type Status = 'all' | 'in' | 'out' | 'closed';
type SortKey = 'pair' | 'state' | 'value' | 'unclaimed' | 'pnl' | 'pct' | 'age';

const sortValue: Record<SortKey, (p: Position) => string | number> = {
  pair: (p) => p.pair, state: (p) => p.state, value: (p) => p.valueUsd, unclaimed: (p) => p.rewards.unclaimedUsd,
  pnl: (p) => p.pnl.netUsd, pct: (p) => p.pnl.pct, age: ageDays,
};

export default function Positions() {
  const w = useOutletContext<WalletResult>();
  const [, setParams] = useSearchParams();
  const [status, setStatus] = useState<Status>('all');
  const [sort, setSort] = useState<{ key: SortKey; dir: 1 | -1 }>({ key: 'value', dir: -1 });

  const rows = useMemo(() => {
    const f = w.positions.filter((p) => status === 'all' || p.state === status);
    return f.sort((a, b) => {
      const x = sortValue[sort.key](a), y = sortValue[sort.key](b);
      return (typeof x === 'string' ? x.localeCompare(y as string) : x - (y as number)) * sort.dir;
    });
  }, [w.positions, status, sort]);

  const th = (key: SortKey, label: string, left?: boolean) => (
    <th className={left ? 'l' : ''} onClick={() => setSort((s) => ({ key, dir: s.key === key ? (-s.dir as 1 | -1) : -1 }))}>
      {label}{sort.key === key ? (sort.dir === 1 ? ' ▲' : ' ▼') : ''}
    </th>
  );

  return (
    <>
      <PageHead title="Positions" sub="Open and closed liquidity positions. Select a row for the full breakdown." />
      <div className="card">
        <div className="card-head">
          <div className="filters">
            <Seg value={status} onChange={setStatus}
              options={[['all', 'All'], ['in', 'In range'], ['out', 'Out of range'], ['closed', 'Closed']]} />
          </div>
          <span className="note">Click a column to sort</span>
        </div>
        <div className="tbl-wrap">
          <table>
            <thead>
              <tr>
                {th('pair', 'Pool', true)}{th('state', 'Status', true)}<th className="l" style={{ cursor: 'default' }}>Price range</th>
                {th('value', 'Value')}{th('unclaimed', 'Unclaimed')}{th('pnl', 'PnL')}{th('pct', 'PnL %')}{th('age', 'Age')}
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => {
                const closed = p.state === 'closed';
                return (
                  <tr key={p.id} tabIndex={0} onClick={() => setParams({ p: p.id })}
                    onKeyDown={(e) => e.key === 'Enter' && setParams({ p: p.id })}>
                    <td className="pair">{p.pair}<small>Aerodrome · {p.chain} · {p.staked ? 'Staked' : 'Unstaked'} · #{p.tokenId}</small></td>
                    <td className="l"><Pill p={p} /></td>
                    <td className="l"><RangeBar p={p} /><small className="note num" style={{ marginLeft: 8 }}>{px(p.priceLower)} – {px(p.priceUpper)}</small></td>
                    <td className="num">{closed ? '—' : usd(p.valueUsd)}</td>
                    <td className="num">{closed ? '—' : usd(p.rewards.unclaimedUsd)}<span className="kind">{p.staked ? 'AERO' : 'fees'}</span></td>
                    <td className={`num ${cls(p.pnl.netUsd)}`} title={rewardLabel(p)}>{sg(p.pnl.netUsd)}{p.incomplete.length > 0 && <span className="inc" title={p.incomplete.join(' ')}> *</span>}</td>
                    <td className={`num ${cls(p.pnl.netUsd)}`}>{arrow(p.pnl.netUsd)} {pc(p.pnl.pct)}</td>
                    <td className="num muted">{ageDays(p)}d</td>
                  </tr>
                );
              })}
              {rows.length === 0 && <tr><td colSpan={8} className="muted l">No positions match these filters.</td></tr>}
            </tbody>
          </table>
        </div>
        <p className="note" style={{ margin: '12px 0 0' }}><span className="inc">*</span> PnL incomplete: gas and, for staked positions, claimed AERO are not tracked yet.</p>
      </div>
    </>
  );
}
