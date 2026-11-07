import type { WalletInfo } from "../lib/wallets";

/** A wallet's name in its own colour, so positions of different wallets can be told apart at a glance. */
export function WalletChip({ wallet }: { wallet: WalletInfo }) {
  return (
    <span
      className="inline-flex items-center gap-1.5 rounded-full px-2 py-px text-xs font-medium"
      style={{ color: wallet.color, background: `color-mix(in srgb, ${wallet.color} 18%, transparent)` }}
      title={wallet.address}
    >
      <span className="h-1.5 w-1.5 rounded-full" style={{ background: wallet.color }} />
      {wallet.name}
    </span>
  );
}

/** Header cell content of a wallet group: the chip, the short address (when it has a name) and how many rows it holds. */
export function WalletGroupLabel({ wallet, count, noun }: { wallet: WalletInfo; count: number; noun: string }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
      <WalletChip wallet={wallet} />
      {wallet.label && <span className="font-mono text-xs font-normal text-slate-500">{wallet.short}</span>}
      <span className="text-xs font-normal text-slate-500">
        · {count} {count === 1 ? noun : `${noun}s`}
      </span>
    </span>
  );
}
