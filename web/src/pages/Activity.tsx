import { useMemo, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import type { PositionEvent, WalletResult } from '../api/client';
import { PageHead, Seg } from '../components/ui';
import { flattenEvents } from '../derive';
import { fmtDate, shortHash, usd } from '../format';

const LABEL: Record<PositionEvent['kind'], string> = { add: 'Add liquidity', remove: 'Remove liquidity', collect: 'Collect' };

export default function Activity() {
  const w = useOutletContext<WalletResult>();
  const [kind, setKind] = useState<'all' | PositionEvent['kind']>('all');
  const events = useMemo(() => flattenEvents(w).filter((e) => kind === 'all' || e.kind === kind).slice(0, 100), [w, kind]);

  return (
    <>
      <PageHead title="Activity" sub="Adds, removals and collections, valued at the time they happened."
        right={<Seg value={kind} onChange={setKind} options={[['all', 'All'], ['add', 'Add'], ['collect', 'Collect'], ['remove', 'Remove']]} />} />
      <div className="card">
        {events.length === 0 && <p className="empty">No activity.</p>}
        {events.map((e) => (
          <div className="ev" key={`${e.txHash}-${e.logIndex}`}>
            <span className="muted num">{fmtDate(e.timestamp, true)}</span>
            <span><span className="tag">{LABEL[e.kind]}</span></span>
            <span>{e.p.pair} <span className="muted">· #{e.p.tokenId}, {e.p.staked ? 'staked' : 'unstaked'}</span></span>
            <span className="num" style={{ textAlign: 'right' }}>{usd(e.usd)}</span>
            <span className="num muted" style={{ textAlign: 'right' }}>
              {w.demo ? shortHash(e.txHash) : <a href={`https://basescan.org/tx/${e.txHash}`} target="_blank" rel="noreferrer">{shortHash(e.txHash)}</a>}
            </span>
          </div>
        ))}
      </div>
    </>
  );
}
