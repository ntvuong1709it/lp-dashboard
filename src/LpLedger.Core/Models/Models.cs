namespace LpLedger.Core.Models;

// Normalized position shape. The web UI reads only this, never protocol-specific data.

public enum PositionState { In, Out, Closed }

public enum EventKind { Add, Remove, Collect }

public enum RewardKind { TradingFees, AeroEmissions }

public sealed record TokenAmount(string Address, string Symbol, int Decimals, double Amount, decimal Usd);

/// <summary>One on-chain liquidity event. Usd is valued at the event's timestamp (unix seconds).</summary>
public sealed record PositionEvent(
    EventKind Kind, long Timestamp, long BlockNumber, string TxHash, int LogIndex,
    double Amount0, double Amount1, decimal Usd);

/// <summary>Staked Aerodrome positions earn AERO emissions; unstaked ones earn trading fees.</summary>
public sealed record Rewards(RewardKind Kind, decimal UnclaimedUsd, IReadOnlyList<TokenAmount> Tokens);

public sealed record PositionPnl(
    decimal NetUsd, decimal Pct, decimal DepositedUsd, decimal HodlUsd,
    decimal PriceEffectUsd,  // what the deposited tokens gained or lost if simply held
    decimal IlUsd,           // pool position value minus hold value
    decimal FeesUsd,         // collected + unclaimed (fees or emissions)
    decimal? GasUsd);        // null until gas tracking is added

public sealed record Position(
    string Id, string Protocol, string Chain, string PositionManager, string TokenId, string PoolAddress, string Pair,
    int TickSpacing, bool Staked, PositionState State,
    double PriceLower, double PriceUpper, double PriceCurrent, double? PriceEntry,
    IReadOnlyList<TokenAmount> Tokens, decimal ValueUsd, Rewards Rewards, PositionPnl Pnl,
    long? OpenedAt, long? ClosedAt, IReadOnlyList<PositionEvent> Events,
    IReadOnlyList<string> Incomplete);  // reasons the PnL is not trustworthy yet

public sealed record Summary(
    int OpenCount, decimal ValueUsd, decimal UnclaimedUsd, decimal DepositedUsd,
    decimal NetPnlUsd, decimal IlUsd, decimal FeesUsd);

public sealed record WalletResult(
    string Address, bool Demo, IReadOnlyList<Position> Positions, Summary Summary, IReadOnlyList<string> Warnings);
