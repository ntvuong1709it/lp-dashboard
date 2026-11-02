export const usd = (n: number, d = 0) =>
  (n < 0 ? '-' : '') + '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });

export const sg = (n: number, d = 0) =>
  (n >= 0 ? '+' : '-') + '$' + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });

export const pc = (n: number) => (n >= 0 ? '+' : '-') + Math.abs(n).toFixed(1) + '%';

export const cls = (n: number) => (n >= 0 ? 'good' : 'bad');

export const arrow = (n: number) => (n >= 0 ? '▲' : '▼');

/** Prices span 0.0001 to 100000, so pick the precision from the magnitude. */
export const px = (n: number) =>
  n >= 100 ? n.toLocaleString('en-US', { maximumFractionDigits: 0 }) : n >= 1 ? n.toFixed(2) : n.toPrecision(3);

export const fmtDate = (unix: number, year = false) =>
  new Date(unix * 1000).toLocaleDateString('en-US', { month: 'short', day: 'numeric', ...(year ? { year: 'numeric' } : {}) });

export const shortAddr = (a: string) => `${a.slice(0, 6)}…${a.slice(-4)}`;

export const shortHash = (h: string) => `${h.slice(0, 8)}…`;

export const amount = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: n < 1 ? 6 : 4 });
