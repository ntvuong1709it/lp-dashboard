import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  DailyEarningsDto,
  PortfolioSummaryDto,
  PositionDetailDto,
  PositionDto,
  PositionStatus,
  ProblemDetails,
  SnapshotPointDto,
  SyncStatus,
  WatchedAddress,
  WeeklyClaimsDto,
} from "./types";

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const problem = (await response.json()) as ProblemDetails;
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Non-JSON error body.
    }
    throw new ApiError(response.status, message);
  }
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T);
}

function query(params: Record<string, string | undefined | null>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export const useAddresses = () =>
  useQuery({
    queryKey: ["addresses"],
    queryFn: () => request<WatchedAddress[]>("/api/addresses"),
  });

export function useAddAddress() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { address: string; label?: string }) =>
      request<WatchedAddress>("/api/addresses", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => client.invalidateQueries(),
  });
}

export function useRemoveAddress() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      request<void>(`/api/addresses/${id}`, { method: "DELETE" }),
    onSuccess: () => client.invalidateQueries(),
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

export function useSyncStatus() {
  const client = useQueryClient();
  return useQuery({
    queryKey: ["sync-status"],
    queryFn: async () => {
      const status = await request<SyncStatus>("/api/sync/status");
      const previous = client.getQueryData<SyncStatus>(["sync-status"]);
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
  return useMutation({
    mutationFn: () => request<SyncStatus>("/api/sync", { method: "POST" }),
    onSuccess: (status) => {
      // Show the queued state immediately; polling picks up the run once it starts.
      client.setQueryData(["sync-status"], status);
      void client.invalidateQueries({ queryKey: ["sync-status"] });
    },
  });
}
