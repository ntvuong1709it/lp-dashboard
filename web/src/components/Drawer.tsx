import { useEffect } from 'react';
import type { Position } from '../api/client';
import { amount, cls, fmtDate, pc, px, sg, shortHash, usd } from '../format';
import { ageDays, rewardLabel } from '../derive';
import { Pill, RangeBar, Waterfall } from './ui';

const KIND_LABEL = { add: 'Add liquidity', remove: 'Remove liquidity', collect: 'Collect' } as const;

export function Drawer({ p, demo, onClose }: { p: Position | undefined; demo: boolean; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const on = p !== undefined;
  const k = p ? rewardLabel(p) : '';
  const steps = p && [
    { label: 'Token price change', v: p.pnl.priceEffectUsd },
    { label: 'Impermanent loss', v: p.pnl.ilUsd },
    { label: p.staked ? 'AERO emissions' : 'Trading fees', v: p.pnl.feesUsd },
    { label: 'Gas spent', v: -(p.pnl.gasUsd ?? 0) },
  ];

  return (
    <>
      <div className={`scrim ${on ? 'on' : ''}`} onClick={onClose} />
      <aside className={`drawer ${on ? 'on' : ''}`} aria-label="Position detail" aria-hidden={!on}>
        {p && steps && (
          <div className="in">
            <div className="page-head">
              <div>
                <h1 style={{ fontSize: 22 }}>{p.pair}</h1>
                <p>Aerodrome · {p.chain} · Slipstream, tick spacing {p.tickSpacing} · #{p.tokenId}</p>
              </div>
              <button className="btn" onClick={onClose}>Close</button>
            </div>
            <div>
              <Pill p={p} />{' '}
              <span className="tag">{p.staked ? 'Staked in gauge' : 'Unstaked'}</span>{' '}
              <span className="muted" style={{ marginLeft: 6 }}>
                {p.state === 'closed' && p.closedAt ? `Closed ${fmtDate(p.closedAt)}` : `Open for ${ageDays(p)} days`}
              </span>
            </div>
            <div className="card">
              <div className="card-head"><h2>Price range</h2><span className="num">Now {px(p.priceCurrent)}</span></div>
              <RangeBar p={p} big />
              <div className="rng-lbl num">
                <span>{px(p.priceLower)}</span>
                <span>{p.priceEntry != null ? `Entry ${px(p.priceEntry)}` : ''}</span>
                <span>{px(p.priceUpper)}</span>
              </div>
              <p className="note" style={{ margin: '8px 0 0' }}>{p.tokens[1].symbol} per {p.tokens[0].symbol}</p>
            </div>
            <div className="mini">
              <div><span>Current value</span><b>{p.state === 'closed' ? '—' : usd(p.valueUsd)}</b></div>
              <div><span>Net PnL</span><b className={cls(p.pnl.netUsd)}>{sg(p.pnl.netUsd)} ({pc(p.pnl.pct)})</b></div>
              <div><span>Unclaimed ({k})</span><b>{usd(p.rewards.unclaimedUsd)}</b></div>
              <div><span>Earns</span><b style={{ fontFamily: 'var(--font-body)' }}>{k}</b></div>
              <div><span>Deposited</span><b>{usd(p.pnl.depositedUsd)}</b></div>
              <div><span>If held instead</span><b>{usd(p.pnl.hodlUsd)}</b></div>
            </div>
            {p.state !== 'closed' && (
              <div className="card">
                <h2 style={{ marginBottom: 10 }}>Holdings</h2>
                <div className="rows">
                  {p.tokens.map((t) => (
                    <div className="bar-row" style={{ gridTemplateColumns: '1fr auto auto' }} key={t.address}>
                      <span>{t.symbol}</span><span className="num">{amount(t.amount)}</span><span className="num muted">{usd(t.usd)}</span>
                    </div>
                  ))}
                </div>
                {p.state === 'out' && (
                  <p className="note" style={{ margin: '10px 0 0' }}>
                    Fully converted to {p.priceCurrent > p.priceUpper ? p.tokens[1].symbol : p.tokens[0].symbol} because price moved{' '}
                    {p.priceCurrent > p.priceUpper ? 'above' : 'below'} the range.
                  </p>
                )}
              </div>
            )}
            <div className="card">
              <div className="card-head"><h2>PnL breakdown</h2></div>
              <Waterfall steps={steps} total={p.pnl.netUsd} />
              {p.incomplete.length > 0 && (
                <p className="inc" style={{ margin: '12px 0 0' }}>PnL incomplete: {p.incomplete.join(' ')}</p>
              )}
            </div>
            <div className="card">
              <h2 style={{ marginBottom: 6 }}>History</h2>
              {p.events.map((e) => (
                <div className="alert" style={{ justifyContent: 'space-between' }} key={`${e.txHash}-${e.logIndex}`}>
                  <span><span className="tag">{KIND_LABEL[e.kind]}</span> <span className="muted">{fmtDate(e.timestamp, true)}</span></span>
                  <span className="num">
                    {usd(e.usd)}{' '}
                    {demo ? <span className="muted">{shortHash(e.txHash)}</span>
                      : <a className="muted" href={`https://basescan.org/tx/${e.txHash}`} target="_blank" rel="noreferrer">{shortHash(e.txHash)}</a>}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </aside>
    </>
  );
}
