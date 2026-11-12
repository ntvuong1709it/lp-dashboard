import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { request } from "./http";
import type { Chain, PoolRiskDto, RiskChangeDto, RiskHeldRowDto, TokenRiskDto } from "./types";

function ownerQuery(owner: string | null) {
  return owner ? `?owner=${encodeURIComponent(owner)}` : "";
}

const riskKeys = {
  token: (chain: Chain, address: string) => ["risk", "token", chain, address.toLowerCase()] as const,
  pool: (poolId: string) => ["risk", "pool", poolId] as const,
};

/** A check made within this long ago just happened, so verdicts cached in the pool and position lists are stale. */
const FRESH_CHECK_MS = 30_000;

function useRefreshListsAfterNewCheck() {
  const client = useQueryClient();
  return (checkedAt: string) => {
    if (Date.now() - new Date(checkedAt).getTime() < FRESH_CHECK_MS) {
      void client.invalidateQueries({ queryKey: ["pools"] });
      void client.invalidateQueries({ queryKey: ["positions"] });
      void client.invalidateQueries({ queryKey: ["risk", "held"] });
    }
  };
}

/**
 * A token's report. A recent check is served from the server's cache; a missing or old one runs now, so this is the lazy
 * check. An address of null leaves the query idle.
 */
export function useTokenRisk(chain: Chain, address: string | null) {
  const afterCheck = useRefreshListsAfterNewCheck();
  return useQuery({
    queryKey: riskKeys.token(chain, address ?? ""),
    enabled: address != null,
    staleTime: 10 * 60_000,
    retry: false,
    queryFn: async () => {
      const report = await request<TokenRiskDto>(`/api/risk/tokens/${chain}/${address}`);
      afterCheck(report.checkedAt);
      return report;
    },
  });
}

/** A pool's report: its own findings and both token reports. Opening a pool runs the lazy check. */
export function usePoolRisk(poolId: string | null) {
  const afterCheck = useRefreshListsAfterNewCheck();
  return useQuery({
    queryKey: riskKeys.pool(poolId ?? ""),
    enabled: poolId != null,
    staleTime: 10 * 60_000,
    retry: false,
    queryFn: async () => {
      const report = await request<PoolRiskDto>(`/api/risk/pools/${encodeURIComponent(poolId!)}`);
      afterCheck(report.checkedAt);
      return report;
    },
  });
}

/** Forces a new check of a token. The server refuses with 429 (and says how long to wait) while the cooldown runs. */
export function useRecheckToken(chain: Chain, address: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => request<TokenRiskDto>(`/api/risk/tokens/${chain}/${address}?refresh=true`),
    onSuccess: (report) => {
      client.setQueryData(riskKeys.token(chain, address), report);
      void client.invalidateQueries({ queryKey: ["pools"] });
      void client.invalidateQueries({ queryKey: ["positions"] });
      void client.invalidateQueries({ queryKey: ["risk", "held"] });
      void client.invalidateQueries({ queryKey: ["risk", "recent"] });
    },
  });
}

export function useRecheckPool(poolId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => request<PoolRiskDto>(`/api/risk/pools/${encodeURIComponent(poolId)}?refresh=true`),
    onSuccess: (report) => {
      client.setQueryData(riskKeys.pool(poolId), report);
      client.setQueryData(riskKeys.token(report.token0.chain, report.token0.address), report.token0);
      client.setQueryData(riskKeys.token(report.token1.chain, report.token1.address), report.token1);
      void client.invalidateQueries({ queryKey: ["pools"] });
      void client.invalidateQueries({ queryKey: ["risk", "held"] });
    },
  });
}

/** Tokens of the wallets' open positions and of starred pools, with cached verdicts. Never triggers a check. */
export const useHeldRisk = (owner: string | null) =>
  useQuery({
    queryKey: ["risk", "held", owner],
    enabled: owner != null,
    queryFn: () => request<RiskHeldRowDto[]>(`/api/risk/held${ownerQuery(owner)}`),
  });

export const useRiskRecent = (owner: string | null) =>
  useQuery({
    queryKey: ["risk", "recent", owner],
    enabled: owner != null,
    queryFn: () => request<RiskChangeDto[]>(`/api/risk/recent${ownerQuery(owner)}`),
  });

/** Follows or unfollows a pool; followed pools join the nightly re-check. */
export function useStarPool() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ poolId, starred }: { poolId: string; starred: boolean }) =>
      request<void>(`/api/pools/${encodeURIComponent(poolId)}/star`, { method: starred ? "PUT" : "DELETE" }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ["pools"] });
      void client.invalidateQueries({ queryKey: ["risk", "held"] });
    },
  });
}
