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
  id: number;
  address: string;
  label: string | null;
  chainFamily: "Evm" | "Solana";
  createdAt: string;
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

export interface SyncStatus {
  isRunning: boolean;
  isQueued: boolean;
  percent: number;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  configuredProviders: string[];
  tasks: SyncTaskStatus[];
  lastErrors: string[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}
