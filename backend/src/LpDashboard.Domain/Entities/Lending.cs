namespace LpDashboard.Domain.Entities;

/// <summary>The latest Aave account figures of a wallet, as read from chain (one row per chain and wallet).</summary>
public class LendingAccount
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public string Owner { get; set; } = string.Empty;
    public decimal TotalCollateralUsd { get; set; }
    public decimal TotalDebtUsd { get; set; }
    /// <summary>Weighted liquidation threshold as a fraction (0.753); the health factor is collateral * threshold / debt.</summary>
    public decimal LiquidationThreshold { get; set; }
    /// <summary>Null when there is no debt.</summary>
    public decimal? HealthFactor { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Last UTC day whose end-of-day balances are stored; history before it is complete.</summary>
    public DateOnly? HistoryThrough { get; set; }
    /// <summary>First UTC day with Aave activity found for the wallet (or where history was cut short).</summary>
    public DateOnly? HistoryFrom { get; set; }
    /// <summary>Set when history could not go back to the wallet's first activity, e.g. an RPC without archive state.</summary>
    public string? HistoryNote { get; set; }
}

/// <summary>One Aave reserve the wallet holds or owes, as of the latest sync.</summary>
public class LendingReserve
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Reserve { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Decimals { get; set; }
    public string PriceKey { get; set; } = string.Empty;
    public decimal SuppliedTokens { get; set; }
    public decimal BorrowedTokens { get; set; }
    public decimal SupplyAprPercent { get; set; }
    public decimal BorrowAprPercent { get; set; }
    public bool IsCollateral { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One reserve at the end of one UTC day (for today: the latest sync). Yield and interest are what the balance earned or
/// accrued that day, worked out from the reserve's indices; whatever else moved the balance was a deposit, withdrawal, borrow or repay.
/// </summary>
public class LendingDay
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Reserve { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal SuppliedTokens { get; set; }
    public decimal BorrowedTokens { get; set; }
    public decimal YieldTokens { get; set; }
    public decimal InterestTokens { get; set; }
    public decimal SupplyIndex { get; set; }
    public decimal BorrowIndex { get; set; }
    public bool IsCollateral { get; set; }
}
