import { useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, Route, Routes, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { isAddress, useWallet } from './api/client';
import { Drawer } from './components/Drawer';
import { shortAddr } from './format';
import Activity from './pages/Activity';
import Features from './pages/Features';
import Overview from './pages/Overview';
import Pnl from './pages/Pnl';
import Positions from './pages/Positions';

const LAST_KEY = 'lp-ledger:address';
const SAMPLE = '0x7a3f000000000000000000000000000000c91e00';

const remembered = () => {
  try { return localStorage.getItem(LAST_KEY) ?? ''; } catch { return ''; }
};
const remember = (a: string) => {
  try { localStorage.setItem(LAST_KEY, a); } catch { /* storage can be blocked */ }
};

function toggleTheme() {
  const r = document.documentElement;
  const dark = r.dataset.theme ? r.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme:dark)').matches;
  r.dataset.theme = dark ? 'light' : 'dark';
}

function Header({ address, demo }: { address?: string; demo?: boolean }) {
  const tabs: [string, string][] = [['', 'Overview'], ['positions', 'Positions'], ['pnl', 'Profit & loss'], ['activity', 'Activity'], ['features', 'Features']];
  return (
    <header>
      <div className="bar">
        <Link to="/" className="logo"><i />LP Ledger</Link>
        {address && (
          <nav aria-label="Sections">
            {tabs.map(([path, label]) => (
              <NavLink key={label} end to={`/${address}${path && '/' + path}`}>{label}</NavLink>
            ))}
          </nav>
        )}
        <span className="spacer" />
        {demo && <span className="mock">Demo data</span>}
        {address && <span className="chip num"><span className="dot" /><Link to="/" title="Change address">{shortAddr(address)}</Link></span>}
        <button className="btn" onClick={toggleTheme} aria-label="Toggle theme">Theme</button>
      </div>
    </header>
  );
}

function Landing() {
  const nav = useNavigate();
  const [value, setValue] = useState(remembered);
  const [touched, setTouched] = useState(false);
  const valid = isAddress(value.trim());
  const submit = (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (valid) { remember(value.trim()); nav(`/${value.trim()}`); }
  };
  return (
    <>
      <Header />
      <main>
        <div className="landing">
          <h1>See your liquidity positions and what they have earned.</h1>
          <p className="muted">Paste a wallet address. Read-only: no wallet connection, no signing, no funds at risk. Aerodrome on Base for now.</p>
          <form className="addr-form" onSubmit={submit}>
            <input value={value} onChange={(e) => setValue(e.target.value)} placeholder="0x…" aria-label="Wallet address"
              aria-invalid={touched && !valid} spellCheck={false} autoFocus />
            <button className="btn primary" type="submit">Look up</button>
          </form>
          {touched && !valid && <p className="err">Enter a 42-character address starting with 0x.</p>}
          <p className="note">No RPC configured yet? Any valid address shows demo data, for example{' '}
            <button className="btn" onClick={() => setValue(SAMPLE)}>use a sample address</button></p>
        </div>
      </main>
    </>
  );
}

function WalletLayout() {
  const { address = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const q = useWallet(address);
  const selected = q.data?.positions.find((p) => p.id === params.get('p'));
  const close = () => setParams((prev) => { prev.delete('p'); return prev; });
  if (isAddress(address)) remember(address);

  return (
    <>
      <Header address={address} demo={q.data?.demo} />
      <main>
        {!isAddress(address) && <div className="banner">That is not a valid address. <Link to="/">Try again</Link></div>}
        {q.isPending && isAddress(address) && (
          <>
            <div className="grid g4"><div className="skeleton" /><div className="skeleton" /><div className="skeleton" /><div className="skeleton" /></div>
            <p className="muted">Reading positions from Base. A first lookup can take a while.</p>
          </>
        )}
        {q.isError && (
          <div className="banner" role="alert">
            <b>Could not load positions.</b> {q.error.message}{' '}
            <button className="btn" onClick={() => q.refetch()}>Retry</button>
          </div>
        )}
        {q.data && (
          <>
            {q.data.demo && (
              <div className="banner demo"><b>Demo data.</b> No Base RPC is configured on the API, so these positions come from a simulated chain, not from this address.</div>
            )}
            {q.data.warnings.map((w) => <div className="banner" key={w}>{w}</div>)}
            <Outlet context={q.data} />
          </>
        )}
      </main>
      <Drawer p={selected} demo={q.data?.demo ?? false} onClose={close} />
    </>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route path="/:address" element={<WalletLayout />}>
        <Route index element={<Overview />} />
        <Route path="positions" element={<Positions />} />
        <Route path="pnl" element={<Pnl />} />
        <Route path="activity" element={<Activity />} />
        <Route path="features" element={<Features />} />
      </Route>
    </Routes>
  );
}
