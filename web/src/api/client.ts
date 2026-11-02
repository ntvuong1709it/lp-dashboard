import { useQuery } from '@tanstack/react-query';
import type { components } from './schema';

export type WalletResult = components['schemas']['WalletResult'];
export type Position = components['schemas']['Position'];
export type PositionEvent = components['schemas']['PositionEvent'];

// Empty in dev (Vite proxies /api); set VITE_API_URL to the Container App URL for a deployed build.
const base = import.meta.env.VITE_API_URL ?? '';

export const isAddress = (s: string) => /^0x[0-9a-fA-F]{40}$/.test(s);

// Open the app with ?demo=true to get simulated positions from the API even when it has an RPC configured.
const forceDemo = () => new URLSearchParams(window.location.search).get('demo') === 'true';

async function fetchWallet(address: string): Promise<WalletResult> {
  const res = await fetch(`${base}/api/wallets/${address}/positions${forceDemo() ? '?demo=true' : ''}`);
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error ?? `Request failed (${res.status})`);
  }
  return res.json();
}

export function useWallet(address: string) {
  return useQuery({
    queryKey: ['wallet', address.toLowerCase()],
    queryFn: () => fetchWallet(address),
    enabled: isAddress(address),
    staleTime: 60_000,
    retry: false,
  });
}
