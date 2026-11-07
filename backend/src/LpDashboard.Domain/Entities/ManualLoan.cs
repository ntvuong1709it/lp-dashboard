namespace LpDashboard.Domain.Entities;

/// <summary>A loan the app cannot read from chain (e.g. Binance), entered by hand.</summary>
public class ManualLoan
{
    public int Id { get; set; }
    /// <summary>The followed wallet this loan belongs to; groups the cost view and scopes the API like positions.</summary>
    public string Owner { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Platform { get; set; } = "Binance";

    /// <summary>Borrowed asset and the price source key used to value it (e.g. "coingecko:tether").</summary>
    public string AssetSymbol { get; set; } = string.Empty;
    public string AssetPriceKey { get; set; } = string.Empty;

    public string? CollateralSymbol { get; set; }
    public string? CollateralPriceKey { get; set; }
    public decimal? MarginCallLtvPercent { get; set; }
    public decimal? LiquidationLtvPercent { get; set; }

    /// <summary>Last day the loan accrues; closed loans leave the cost view and the current totals.</summary>
    public DateOnly? ClosedOn { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ManualLoanTerm> Terms { get; set; } = [];
    public List<ManualLoanCollateral> Collateral { get; set; } = [];
}

/// <summary>
/// From <see cref="EffectiveFrom"/> the loan owes <see cref="Principal"/> (in borrowed-asset units, the total debt shown by the
/// lender at that time) and accrues simple interest at <see cref="AprPercent"/>. A rate change, top-up or repayment is a new row.
/// </summary>
public class ManualLoanTerm
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal Principal { get; set; }
    public decimal AprPercent { get; set; }
}

/// <summary>From <see cref="EffectiveFrom"/> the loan is backed by <see cref="Amount"/> units of the collateral asset.</summary>
public class ManualLoanCollateral
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal Amount { get; set; }
}
