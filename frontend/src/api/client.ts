import {
  keepPreviousData,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useOwner } from "../lib/owner";
import type {
  ActivityEventDto,
  AssetOptionDto,
  BorrowSummaryDto,
  ChangesDto,
  Chain,
  CollateralHistoryDto,
  DailyCoverageDto,
  DailyEarningsDto,
  LoanDto,
  ManualLoanDto,
  PositionEarningsDto,
  HealthDto,
  PnlHistoryDto,
  PoolEstimateDto,
  PoolHistorySeriesDto,
  PoolListDto,
  PortfolioHistoryPoint,
  PortfolioInsightsDto,
  PortfolioPerformanceDto,
  SyncRequestBody,
  PortfolioSummaryDto,
  PositionDetailDto,
  PositionDto,
  PositionStatus,
  SnapshotPointDto,
  SyncStatus,
  TickerDto,
  WatchedAddress,
  WeeklyClaimsDto,
} from "./types";
import { request } from "./http";

export { ApiError } from "./http";

function query(params: Record<string, string | undefined | null>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** Adds a wallet (with an optional label) to the account's list, which every device shows. Idempotent. */
export function useAddAddress() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { address: string; label?: string }) =>
      request<WatchedAddress>("/api/addresses", {
        method: "POST",
        body: JSON.stringify({ address: body.address, label: body.label }),
      }),
    onSuccess: () => void client.invalidateQueries(),
  });
}

export const usePositions = (
  owner: string | null,
  status: PositionStatus | null,
) =>
  useQuery({
    queryKey: ["positions", owner, status],
    queryFn: () =>
      request<PositionDto[]>(`/api/positions${query({ owner, status })}`),
  });

export const useSummary = (
  owner: string | null,
  status: PositionStatus | null,
) =>
  useQuery({
    queryKey: ["summary", owner, status],
    queryFn: () =>
      request<PortfolioSummaryDto>(
        `/api/portfolio/summary${query({ owner, status })}`,
      ),
  });

export const usePortfolioHistory = (owner: string | null) =>
  useQuery({
    queryKey: ["portfolio-history", owner],
    queryFn: () =>
      request<PortfolioHistoryPoint[]>(
        `/api/portfolio/history${query({ owner })}`,
      ),
  });

export const usePnlHistory = (owner: string | null) =>
  useQuery({
    queryKey: ["pnl-history", owner],
    queryFn: () =>
      request<PnlHistoryDto>(`/api/portfolio/pnl-history${query({ owner })}`),
  });

export const useActivity = (owner: string | null, limit = 500) =>
  useQuery({
    queryKey: ["activity", owner, limit],
    queryFn: () =>
      request<ActivityEventDto[]>(`/api/events${query({ owner, limit: String(limit) })}`),
  });

export const usePosition = (id: number) =>
  useQuery({
    queryKey: ["position", id],
    queryFn: () => request<PositionDetailDto>(`/api/positions/${id}`),
  });

export const useSnapshots = (id: number) =>
  useQuery({
    queryKey: ["snapshots", id],
    queryFn: () =>
      request<SnapshotPointDto[]>(`/api/positions/${id}/snapshots`),
  });

export const useWeeklyClaims = (owner: string | null) =>
  useQuery({
    queryKey: ["claims", owner, timeZone],
    queryFn: () =>
      request<WeeklyClaimsDto[]>(
        `/api/fees/claims${query({ owner, timeZone })}`,
      ),
  });

export const usePositionDailyEarnings = (id: number) =>
  useQuery({
    queryKey: ["position-daily-earnings", id, timeZone],
    queryFn: () =>
      request<DailyEarningsDto[]>(
        `/api/positions/${id}/earnings/daily${query({ timeZone })}`,
      ),
  });

export const useDailyEarnings = (owner: string | null) =>
  useQuery({
    queryKey: ["daily-earnings", owner, timeZone],
    queryFn: () =>
      request<DailyEarningsDto[]>(
        `/api/fees/daily${query({ owner, timeZone })}`,
      ),
  });

export const useEarningsByPosition = (owner: string | null) =>
  useQuery({
    queryKey: ["earnings-by-position", owner, timeZone],
    queryFn: () =>
      request<PositionEarningsDto[]>(
        `/api/fees/by-position${query({ owner, timeZone })}`,
      ),
  });

export const useHealth = () =>
  useQuery({
    queryKey: ["health"],
    queryFn: () => request<HealthDto>("/api/health"),
    refetchInterval: 60000,
  });

/** Headline asset prices for the header; the server caches them for a minute. */
export const usePriceTicker = () =>
  useQuery({
    queryKey: ["price-ticker"],
    queryFn: () => request<TickerDto>("/api/prices/ticker"),
    refetchInterval: 60_000,
    retry: false,
  });

export function useSyncStatus() {
  const client = useQueryClient();
  const { allOwners } = useOwner();
  const key = ["sync-status", allOwners];
  return useQuery({
    queryKey: key,
    queryFn: async () => {
      const status = await request<SyncStatus>(
        `/api/sync/status${query({ owner: allOwners })}`,
      );
      const previous = client.getQueryData<SyncStatus>(key);
      if (previous && previous.lastFinishedAt !== status.lastFinishedAt) {
        await client.invalidateQueries({
          predicate: (q) => q.queryKey[0] !== "sync-status",
        });
      }
      return status;
    },
    refetchInterval: (q) =>
      q.state.data?.isRunning || q.state.data?.isQueued ? 1000 : 30000,
  });
}

export function useTriggerSync() {
  const client = useQueryClient();
  const { allOwners } = useOwner();
  return useMutation({
    mutationFn: (scope?: SyncRequestBody) =>
      request<SyncStatus>("/api/sync", {
        method: "POST",
        // Without a narrower scope this syncs every wallet the browser follows.
        body: JSON.stringify({ owner: allOwners, ...scope }),
      }),
    onSuccess: (status) => {
      // Show the queued state immediately; polling picks up the run once it starts.
      client.setQueryData(["sync-status", allOwners], status);
      void client.invalidateQueries({ queryKey: ["sync-status"] });
    },
  });
}

export const usePools = (owner: string | null, minTvl: number, chain: Chain = "Base") =>
  useQuery({
    queryKey: ["pools", owner, minTvl, chain],
    staleTime: 5 * 60_000,
    queryFn: () =>
      request<PoolListDto>(
        `/api/pools${query({ owner, chain, minTvl: String(minTvl), limit: "1000" })}`,
      ),
  });

/** Last 30 days of TVL, fee APR and derived fees and volume. `feePercent` is the fee the list shows for the pool. */
export const usePoolHistory = (poolId: string | null, feePercent: number | null) =>
  useQuery({
    queryKey: ["pool-history", poolId, feePercent],
    enabled: poolId != null,
    staleTime: 30 * 60_000,
    retry: false,
    queryFn: () =>
      request<PoolHistorySeriesDto>(
        `/api/pools/${encodeURIComponent(poolId!)}/history${query({ feePercent: feePercent == null ? null : String(feePercent) })}`,
      ),
  });

export const usePoolEstimate = (
  poolId: string | null,
  halfWidthPercent: number,
  capitalUsd: number,
  days: number,
  feePercent: number | null,
  /** On-chain fees of pools you hold, so the "other pools" list uses the same fee the table does (dynamic-fee pools differ from the source's). */
  knownFees: Record<string, number> = {},
) =>
  useQuery({
    queryKey: ["pool-estimate", poolId, halfWidthPercent, capitalUsd, days, feePercent, JSON.stringify(knownFees)],
    enabled: poolId != null,
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: () =>
      request<PoolEstimateDto>(
        `/api/pools/${encodeURIComponent(poolId!)}/estimate`,
        {
          method: "POST",
          body: JSON.stringify({ halfWidthPercent, capitalUsd, days, feePercent, knownFees }),
        },
      ),
  });

export const usePortfolioInsights = (owner: string | null) =>
  useQuery({
    queryKey: ["portfolio-insights", owner],
    queryFn: () =>
      request<PortfolioInsightsDto>(`/api/portfolio/insights${query({ owner })}`),
  });

// ---- Borrow ----

export const useBorrowSummary = (owner: string | null, days: number) =>
  useQuery({
    queryKey: ["borrow", "summary", owner, days, timeZone],
    enabled: owner != null,
    queryFn: () =>
      request<BorrowSummaryDto>(
        `/api/borrow/summary${query({ owner, days: String(days), timeZone })}`,
      ),
  });

export const useBorrowLoans = (owner: string | null, days: number) =>
  useQuery({
    queryKey: ["borrow", "loans", owner, days, timeZone],
    enabled: owner != null,
    queryFn: () =>
      request<LoanDto[]>(
        `/api/borrow/loans${query({ owner, days: String(days), timeZone })}`,
      ),
  });

export const useBorrowDaily = (owner: string | null, days: number) =>
  useQuery({
    queryKey: ["borrow", "daily", owner, days, timeZone],
    enabled: owner != null,
    queryFn: () =>
      request<DailyCoverageDto[]>(
        `/api/borrow/daily${query({ owner, days: String(days), timeZone })}`,
      ),
  });

export const useCollateralHistory = (owner: string | null, days: number, loan: string) =>
  useQuery({
    queryKey: ["borrow", "collateral-history", owner, days, loan, timeZone],
    enabled: owner != null,
    placeholderData: keepPreviousData,
    queryFn: () =>
      request<CollateralHistoryDto>(
        `/api/borrow/collateral-history${query({ owner, days: String(days), timeZone, loan })}`,
      ),
  });

export const useBorrowChanges = (owner: string | null, days: number, loan: string) =>
  useQuery({
    queryKey: ["borrow", "changes", owner, days, loan, timeZone],
    enabled: owner != null,
    placeholderData: keepPreviousData,
    queryFn: () =>
      request<ChangesDto>(
        `/api/borrow/changes${query({ owner, days: String(days), timeZone, loan })}`,
      ),
  });

/** The same figures as useBorrowChanges, one request per wallet (shares the cache when a single wallet is picked). */
export const useBorrowChangesByWallet = (owners: string[], days: number, loan: string) =>
  useQueries({
    queries: owners.map((owner) => ({
      queryKey: ["borrow", "changes", owner, days, loan, timeZone],
      placeholderData: keepPreviousData,
      queryFn: () =>
        request<ChangesDto>(
          `/api/borrow/changes${query({ owner, days: String(days), timeZone, loan })}`,
        ),
    })),
  });

export const useManualLoans =(owner: string | null) =>
  useQuery({
    queryKey: ["borrow", "manual", owner],
    enabled: owner != null,
    queryFn: () =>
      request<ManualLoanDto[]>(`/api/borrow/loans/manual${query({ owner })}`),
  });

export const useBorrowAssets = () =>
  useQuery({
    queryKey: ["borrow", "assets"],
    staleTime: Infinity,
    queryFn: () => request<AssetOptionDto[]>("/api/borrow/assets"),
  });

/** Creates a manual loan (no id) or edits one (with id); both refresh every borrow figure. */
export function useSaveManualLoan() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (loan: ManualLoanDto) =>
      loan.id > 0
        ? request<ManualLoanDto>(
            `/api/borrow/loans/manual/${loan.id}${query({ owner: loan.owner })}`,
            { method: "PUT", body: JSON.stringify(loan) },
          )
        : request<ManualLoanDto>("/api/borrow/loans/manual", {
            method: "POST",
            body: JSON.stringify(loan),
          }),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["borrow"] }),
  });
}

export function useDeleteManualLoan() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (loan: { id: number; owner: string }) =>
      request<void>(
        `/api/borrow/loans/manual/${loan.id}${query({ owner: loan.owner })}`,
        { method: "DELETE" },
      ),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["borrow"] }),
  });
}

export const usePerformance = (owner: string | null) =>
  useQuery({
    queryKey: ["performance", owner],
    // Pool rates come from an external source; a slow answer should not retrigger on every focus.
    staleTime: 60_000,
    queryFn: () =>
      request<PortfolioPerformanceDto>(`/api/portfolio/performance${query({ owner })}`),
  });
