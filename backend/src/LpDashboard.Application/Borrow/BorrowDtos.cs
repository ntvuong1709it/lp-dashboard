using LpDashboard.Domain.Metrics;

namespace LpDashboard.Application.Borrow;

public sealed record AssetOptionDto(string Symbol, string PriceKey, bool Stable);

public sealed record TermDto(DateOnly EffectiveFrom, decimal Principal, decimal AprPercent);

public sealed record CollateralRowDto(DateOnly EffectiveFrom, decimal Amount);

/// <summary>What the API returns and accepts for a manual loan (<see cref="Id"/> is ignored on create).</summary>
public sealed record ManualLoanDto(
    int Id,
    string Owner,
    string Label,
    string? Platform,
    string AssetSymbol,
    string? AssetPriceKey,
    string? CollateralSymbol,
    string? CollateralPriceKey,
    decimal? MarginCallLtvPercent,
    decimal? LiquidationLtvPercent,
    DateOnly? ClosedOn,
    IReadOnlyList<TermDto> Terms,
    IReadOnlyList<CollateralRowDto>? Collateral);

public sealed record LoanCostDto(decimal PerDayUsd, decimal PerWeekUsd, decimal PerMonthUsd, decimal PerYearUsd);

/// <summary><see cref="Kind"/> is "HealthFactor", "Ltv" or "None".</summary>
public sealed record LoanRiskDto(
    string Kind,
    decimal? Value,
    RiskLevel Level,
    decimal? DropToLiquidationPercent,
    decimal? LiquidationLtvPercent,
    decimal? MarginCallLtvPercent);

/// <summary>One loan today. <see cref="Kind"/> is "aave" or "manual".</summary>
public sealed record LoanDto(
    string Key,
    string Kind,
    string Source,
    string Label,
    string Owner,
    string DebtSymbol,
    int? ManualId,
    decimal DebtTokens,
    decimal DebtUsd,
    decimal AprPercent,
    decimal InterestInWindowUsd,
    LoanCostDto Cost,
    decimal? CollateralUsd,
    decimal? CollateralRatio,
    decimal? CollateralRatioChange,
    LoanRiskDto Risk,
    bool HasMissingPrices);

public sealed record CoverageDto(
    decimal LpFeesUsd,
    decimal LpRewardsUsd,
    decimal SupplyYieldUsd,
    decimal InterestUsd,
    decimal GasUsd,
    decimal LpIncomeUsd,
    decimal NetCarryUsd,
    decimal? Ratio,
    CoverageStatus Status);

public sealed record RunRateDto(
    decimal InterestPerDayUsd,
    decimal IncomePerDayUsd,
    decimal LpFeesAndRewardsPerDayUsd,
    decimal OpenPositionValueUsd,
    decimal? RequiredLpAprPercent,
    decimal? CurrentLpAprPercent);

public sealed record WalletCostsDto(string Owner, IReadOnlyList<string> LoanKeys, LoanCostDto Subtotal);

/// <summary><see cref="IncomeAtPace"/> is <see cref="LpIncomeAtPace"/> plus <see cref="SupplyYieldAtRate"/>; <see cref="LeftOver"/> is that minus <see cref="Total"/>.</summary>
public sealed record CostsDto(
    IReadOnlyList<WalletCostsDto> Wallets,
    LoanCostDto Total,
    LoanCostDto LpIncomeAtPace,
    LoanCostDto SupplyYieldAtRate,
    LoanCostDto IncomeAtPace,
    LoanCostDto LeftOver);

public sealed record RepayDto(RepayMode Mode, decimal AmountUsd, string? LoanLabel, decimal AprPercent, decimal SavingPerYearUsd);

public sealed record TrendHeadlineDto(
    decimal CollateralUsd,
    decimal DebtUsd,
    decimal? RatioNow,
    decimal? RatioStart,
    decimal? LtvPercentNow,
    /// <summary>Loans left out of the trend because they carry no collateral data.</summary>
    IReadOnlyList<string> ExcludedLoans);

/// <summary><see cref="HistoryNotes"/> say why a wallet's Aave history is shorter than expected (e.g. an RPC without historical state).</summary>
public sealed record DataQualityDto(bool HasMissingPrices, IReadOnlyList<string> MissingPriceKeys, IReadOnlyList<string> HistoryNotes);

public sealed record BorrowSummaryDto(
    DateOnly From,
    DateOnly To,
    int Days,
    bool HasLoans,
    CoverageDto Coverage,
    RunRateDto RunRate,
    CostsDto Costs,
    LoanRiskDto? WorstRisk,
    string? WorstRiskLabel,
    RepayDto Repay,
    TrendHeadlineDto Trend,
    DataQualityDto DataQuality);

public sealed record DailyCoverageDto(
    DateOnly Date,
    decimal FeesUsd,
    decimal RewardsUsd,
    decimal SupplyYieldUsd,
    decimal InterestUsd,
    decimal GasUsd,
    decimal NetUsd);

public sealed record TrendPointDto(DateOnly Date, decimal CollateralUsd, decimal DebtUsd, decimal? Ratio, decimal? LtvPercent);

public sealed record TrendEventDto(DateOnly Date, TrendEventKind Kind, decimal Usd, string Label);

public sealed record CollateralHistoryDto(
    IReadOnlyList<TrendPointDto> Points,
    IReadOnlyList<TrendEventDto> Events,
    /// <summary>Collateral / debt at which a single loan reaches its warning level and its liquidation; null for several loans.</summary>
    decimal? WarnRatio,
    decimal? LiquidationRatio,
    IReadOnlyList<string> ExcludedLoans);

public sealed record ChangesDto(
    DateOnly From,
    DateOnly To,
    decimal CollateralStartUsd,
    decimal PriceMovesUsd,
    decimal DepositsUsd,
    decimal SupplyYieldUsd,
    decimal CollateralEndUsd,
    decimal DebtStartUsd,
    decimal InterestUsd,
    decimal BorrowedUsd,
    decimal DebtPriceMovesUsd,
    decimal DebtEndUsd,
    decimal? RatioStart,
    decimal? RatioEnd);
