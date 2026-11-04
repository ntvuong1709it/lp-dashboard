export type Chain = "Base" | "Ethereum" | "Solana" | "Robinhood";
export type Protocol =
  | "UniswapV3"
  | "AerodromeSlipstream"
  | "UniswapV4"
  | "OrcaWhirlpool"
  | "RaydiumClmm"
  | "MeteoraDlmm";
export type PositionStatus = "Open" | "Closed";
export type PositionEventType =
  | "IncreaseLiquidity"
  | "DecreaseLiquidity"
  | "Collect"
  | "RewardClaim";

export interface WatchedAddress {
  address: string;
  chainFamily: "Evm" | "Solana";
}

export interface TokenDto {
  address: string;
  symbol: string;
  decimals: number;
}

/** Prices are token0 denominated in token1. */
export interface RangeDto {
  priceLower: number;
  priceUpper: number;
  priceCurrent: number | null;
  inRange: boolean | null;
  distanceToLowerPercent: number | null;
  distanceToUpperPercent: number | null;
}

export interface PositionMetrics {
  deposited0: number;
  deposited1: number;
  withdrawn0: number;
  withdrawn1: number;
  collectedFee0: number;
  collectedFee1: number;
  depositedUsd: number;
  withdrawnUsd: number;
  collectedFeesUsd: number;
  uncollectedFeesUsd: number;
  claimedRewardsUsd: number;
  pendingRewardsUsd: number;
  claimedRewardAmount: number;
  rewardPriceUsd: number | null;
  feesEarnedUsd: number;
  currentValueUsd: number;
  gasUsd: number;
  pnlUsd: number;
  pnlPercent: number | null;
  hodlValueUsd: number;
  impermanentLossUsd: number;
  pnlVsHodlUsd: number;
  feeAprPercent: number | null;
  ageDays: number | null;
  hasMissingPrices: boolean;
  hasEstimatedPrices: boolean;
}

/**
 * Measured from snapshots (the real pool tick); time without snapshots is estimated from the tick implied by
 * historical token prices, so `estimatedHours > 0` means the percentage is partly an estimate.
 */
export interface TimeInRangeDto {
  percent: number;
  measuredHours: number;
  estimatedHours: number;
  /** measuredHours + estimatedHours */
  trackedHours: number;
  spanHours: number;
}

export interface PortfolioHistoryPoint {
  timestamp: string;
  valueUsd: number;
  unclaimedUsd: number;
  positionCount: number;
}

export interface PositionDto {
  id: number;
  chain: Chain;
  protocol: Protocol;
  tokenId: string;
  owner: string;
  status: PositionStatus;
  isStaked: boolean;
  poolAddress: string | null;
  fee: number | null;
  tickSpacing: number | null;
  token0: TokenDto | null;
  token1: TokenDto | null;
  tickLower: number;
  tickUpper: number;
  currentTick: number | null;
  range: RangeDto | null;
  openedAt: string | null;
  closedAt: string | null;
  amount0: number;
  amount1: number;
  uncollected0: number;
  uncollected1: number;
  pendingReward: number;
  rewardTokenSymbol: string | null;
  price0Usd: number | null;
  price1Usd: number | null;
  valuedAt: string | null;
  metrics: PositionMetrics;
  /** Null until at least two snapshots exist for the position. */
  timeInRange: TimeInRangeDto | null;
}

export interface PositionEventDto {
  id: number;
  type: PositionEventType;
  txHash: string;
  blockNumber: number;
  timestamp: string;
  amount0: number;
  amount1: number;
  principal0: number;
  principal1: number;
  fee0: number;
  fee1: number;
  rewardTokenSymbol: string | null;
  rewardAmount: number;
  price0Usd: number | null;
  price1Usd: number | null;
  rewardPriceUsd: number | null;
  valueUsd: number | null;
  gasUsd: number | null;
}

/** A position event with enough context to list it across positions. */
export interface ActivityEventDto {
  positionId: number;
  chain: Chain;
  protocol: Protocol;
  tokenId: string;
  pair: string;
  event: PositionEventDto;
}

export interface PositionDetailDto {
  position: PositionDto;
  events: PositionEventDto[];
}

export interface PortfolioSummaryDto {
  openCount: number;
  closedCount: number;
  currentValueUsd: number;
  depositedUsd: number;
  feesEarnedUsd: number;
  uncollectedFeesUsd: number;
  pendingRewardsUsd: number;
  pnlUsd: number;
  pnlVsHodlUsd: number;
  impermanentLossUsd: number;
  gasUsd: number;
  hasMissingPrices: boolean;
}

export interface SnapshotPointDto {
  timestamp: string;
  valueUsd: number;
  valueWithFeesUsd: number;
  hodlValueUsd: number;
  price0Usd: number | null;
  price1Usd: number | null;
  currentTick: number;
}

export interface FeeClaimItemDto {
  positionId: number;
  tokenId: string;
  protocol: Protocol;
  pair: string;
  type: PositionEventType;
  txHash: string;
  timestamp: string;
  fee0: number;
  fee1: number;
  token0Symbol: string | null;
  token1Symbol: string | null;
  rewardAmount: number;
  rewardTokenSymbol: string | null;
  feesUsd: number;
  rewardsUsd: number;
  totalUsd: number;
  hasMissingPrices: boolean;
}

export interface DailyEarningsDto {
  date: string;
  feesUsd: number;
  rewardsUsd: number;
  totalUsd: number;
  hasMissingPrices: boolean;
}

export interface WeeklyClaimsDto {
  weekStart: string;
  feesUsd: number;
  rewardsUsd: number;
  totalUsd: number;
  claimCount: number;
  items: FeeClaimItemDto[];
}

export type SyncStage =
  | "FetchingLatestBlock"
  | "DiscoveringPositions"
  | "FetchingEvents"
  | "SyncingPositions"
  | "Saving";

export type SyncTaskState =
  | "Pending"
  | "Running"
  | "Succeeded"
  | "CompletedWithErrors"
  | "Failed";

export interface SyncTaskStatus {
  chain: Chain;
  protocol: Protocol;
  owner: string;
  /** Set for partial runs (one wallet, one position, rescan); null for a full sync. */
  scope: string | null;
  state: SyncTaskState;
  stage: SyncStage | null;
  detail: string | null;
  detailDone: number;
  detailTotal: number;
  positionsDone: number;
  positionsTotal: number;
  percent: number;
  startedAt: string | null;
  finishedAt: string | null;
  errors: string[];
}

export interface RecentSyncError {
  at: string;
  message: string;
}

/** What a manual sync covers; omit everything for a full sync. */
export interface SyncRequestBody {
  owner?: string;
  chain?: Chain;
  protocol?: Protocol;
  tokenId?: string;
  fromBlock?: number;
}

export interface HealthDto {
  status: "Healthy" | "Degraded" | "Unhealthy";
  databaseOk: boolean;
  providers: string[];
  isSyncing: boolean;
  lastSyncFinishedAt: string | null;
  lastSyncErrorCount: number;
  backupEnabled: boolean;
}

export interface SyncStatus {
  isRunning: boolean;
  isQueued: boolean;
  percent: number;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  configuredProviders: string[];
  tasks: SyncTaskStatus[];
  lastErrors: string[];
  recentErrors: RecentSyncError[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

export interface PnlHistoryDto {
  points: { timestamp: string; cumulativePnlUsd: number }[];
  months: { month: string; netPnlUsd: number }[];
}

export interface PoolDto {
  id: string;
  chain: Chain;
  protocol: Protocol;
  symbol: string;
  token0: string;
  token1: string;
  feePercent: number | null;
  tickSpacing: number | null;
  tvlUsd: number;
  volume1dUsd: number | null;
  volume7dAvgUsd: number | null;
  volumeToTvl: number | null;
  feeApr1d: number | null;
  feeApr7d: number | null;
  rewardApr: number | null;
  held: boolean;
  /** Half-width of your open position in this pool, as if centred on the price. */
  myHalfWidthPercent: number | null;
}

export interface PoolListDto {
  pools: PoolDto[];
  total: number;
  fetchedAt: string;
}

export interface PoolEstimateDto {
  poolId: string;
  basis: "7d" | "24h";
  volatility: number;
  referenceHalfWidthPercent: number;
  concentration: number;
  timeInRange: number;
  apr: number;
  aprLow: number;
  aprHigh: number;
  feesUsd: number;
  depositSharePercent: number;
  rewardApr: number | null;
  priceCurrent: number;
  priceLower: number;
  priceUpper: number;
  feesPercentOfCapital: number;
  breakEvenDownPercent: number | null;
  breakEvenUpPercent: number | null;
  history: PoolHistoryDto;
  alternatives: PoolAlternativeDto[];
}

export interface PoolHistoryDto {
  daysInRange: number;
  days: number;
  firstExitDay: number | null;
  lowPercent: number;
  highPercent: number;
}

export interface PoolAlternativeDto {
  id: string;
  protocol: Protocol;
  feePercent: number | null;
  tickSpacing: number | null;
  tvlUsd: number;
  apr: number;
  isSelected: boolean;
}
