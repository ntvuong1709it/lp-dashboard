import { useQuery } from "@tanstack/react-query";
import { request } from "./http";
import type { MarketWatchDto } from "./types";

const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/**
 * Upcoming macro events and earnings, recent results with a risk tag, headlines, and the stress table of the loan with
 * the worst risk. The calendar does not depend on the wallet; the owner only adds the loan, so it is not required.
 * The first load can take several seconds while the server fills its cache.
 */
export const useMarketWatch = (owner: string | null, days: 7 | 14) =>
  useQuery({
    queryKey: ["market", owner, days, timeZone],
    staleTime: 5 * 60_000,
    refetchInterval: 10 * 60_000,
    queryFn: () => {
      const search = new URLSearchParams({ days: String(days), timeZone });
      if (owner) search.set("owner", owner);
      return request<MarketWatchDto>(`/api/market/watch?${search.toString()}`);
    },
  });
