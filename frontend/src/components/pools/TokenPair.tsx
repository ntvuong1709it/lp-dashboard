import { tokenSymbols } from "../../lib/pools";

const TONES = [
  "bg-accent/20 text-accent",
  "bg-up/20 text-up",
  "bg-warn/20 text-warn",
  "bg-down/20 text-down",
];

function tone(symbol: string) {
  let h = 0;
  for (const c of symbol) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return TONES[h % TONES.length];
}

function Avatar({ symbol, size }: { symbol: string; size: number }) {
  return (
    <span
      aria-hidden="true"
      style={{ width: size, height: size, fontSize: size * 0.38 }}
      className={`inline-flex shrink-0 items-center justify-center rounded-full border border-panel font-semibold ${tone(symbol)}`}
    >
      {symbol.replace(/^[wW](?=[A-Z])/, "").slice(0, 2).toUpperCase()}
    </span>
  );
}

/**
 * Two overlapping letter avatars for the pair. The pool source has no token logos, so the letters stand in; the
 * colour is derived from the symbol so a token looks the same everywhere.
 */
export function TokenPair({ symbol, size = 28 }: { symbol: string; size?: number }) {
  const [a, b] = tokenSymbols(symbol);
  return (
    <span className="inline-flex shrink-0 items-center">
      <Avatar symbol={a} size={size} />
      {b && (
        <span style={{ marginLeft: -size * 0.3 }}>
          <Avatar symbol={b} size={size} />
        </span>
      )}
    </span>
  );
}
