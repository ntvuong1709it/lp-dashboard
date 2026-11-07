using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Application.Borrow;

public sealed class BorrowValidationException(string message) : Exception(message);

/// <summary>Creates, edits and lists manual loans. A wallet only ever sees the loans of the wallets it names.</summary>
public sealed class ManualLoanService(IManualLoanRepository loans, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<IReadOnlyList<ManualLoanDto>> ListAsync(IReadOnlyCollection<string> owners, CancellationToken ct) =>
        (await loans.ListAsync(owners, ct)).Select(ToDto).ToList();

    public async Task<ManualLoanDto> CreateAsync(ManualLoanDto input, CancellationToken ct)
    {
        var loan = new ManualLoan { CreatedAt = time.GetUtcNow().UtcDateTime };
        Apply(loan, input);
        loans.Add(loan);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(loan);
    }

    /// <summary>Null when the loan does not exist or belongs to another wallet.</summary>
    public async Task<ManualLoanDto?> UpdateAsync(int id, string owner, ManualLoanDto input, CancellationToken ct)
    {
        var loan = await loans.GetAsync(id, ct);
        if (loan is null || !loan.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)) return null;

        Apply(loan, input with { Owner = loan.Owner });
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(loan);
    }

    public async Task<bool> DeleteAsync(int id, string owner, CancellationToken ct)
    {
        var loan = await loans.GetAsync(id, ct);
        if (loan is null || !loan.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)) return false;

        loans.Remove(loan);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static void Apply(ManualLoan loan, ManualLoanDto input)
    {
        if (AddressParser.TryParse(input.Owner) is not { } owner)
            throw new BorrowValidationException("Pick the wallet this loan belongs to.");

        var label = input.Label?.Trim() ?? "";
        if (label.Length is 0 or > 100) throw new BorrowValidationException("Give the loan a label of up to 100 characters.");

        var assetSymbol = input.AssetSymbol?.Trim().ToUpperInvariant() ?? "";
        if (assetSymbol.Length is 0 or > 32) throw new BorrowValidationException("Choose the borrowed asset.");
        var assetKey = ResolveKey(input.AssetPriceKey, assetSymbol)
            ?? throw new BorrowValidationException($"No price source for {assetSymbol}. Enter a price key such as coingecko:<id>.");

        string? collateralSymbol = null, collateralKey = null;
        if (!string.IsNullOrWhiteSpace(input.CollateralSymbol))
        {
            collateralSymbol = input.CollateralSymbol.Trim().ToUpperInvariant();
            if (collateralSymbol.Length > 32) throw new BorrowValidationException("The collateral asset name is too long.");
            collateralKey = ResolveKey(input.CollateralPriceKey, collateralSymbol)
                ?? throw new BorrowValidationException($"No price source for {collateralSymbol}. Enter a price key such as coingecko:<id>.");
        }

        if (input.Terms is not { Count: > 0 }) throw new BorrowValidationException("Add at least one row of terms: a date, the debt and the APR.");
        foreach (var t in input.Terms)
        {
            if (t.Principal <= 0) throw new BorrowValidationException("Each terms row needs a debt above zero.");
            if (t.AprPercent is < 0 or > 1000) throw new BorrowValidationException("APR must be between 0 and 1000 percent.");
        }
        if (input.Terms.Select(t => t.EffectiveFrom).Distinct().Count() != input.Terms.Count)
            throw new BorrowValidationException("Two terms rows share the same date.");

        var collateralRows = input.Collateral ?? [];
        foreach (var c in collateralRows)
        {
            if (c.Amount < 0) throw new BorrowValidationException("Collateral amounts cannot be negative.");
        }
        if (collateralRows.Select(c => c.EffectiveFrom).Distinct().Count() != collateralRows.Count)
            throw new BorrowValidationException("Two collateral rows share the same date.");

        foreach (var ltv in new[] { input.MarginCallLtvPercent, input.LiquidationLtvPercent })
        {
            if (ltv is <= 0 or > 100) throw new BorrowValidationException("LTV limits are percentages between 0 and 100.");
        }
        if (input.MarginCallLtvPercent is { } margin && input.LiquidationLtvPercent is { } liq && margin >= liq)
            throw new BorrowValidationException("The margin call LTV must be below the liquidation LTV.");

        loan.Owner = owner.Address;
        loan.Label = label;
        loan.Platform = string.IsNullOrWhiteSpace(input.Platform) ? "Other" : input.Platform.Trim();
        loan.AssetSymbol = assetSymbol;
        loan.AssetPriceKey = assetKey;
        loan.CollateralSymbol = collateralSymbol;
        loan.CollateralPriceKey = collateralKey;
        loan.MarginCallLtvPercent = input.MarginCallLtvPercent;
        loan.LiquidationLtvPercent = input.LiquidationLtvPercent;
        loan.ClosedOn = input.ClosedOn;

        loan.Terms.Clear();
        loan.Terms.AddRange(input.Terms.OrderBy(t => t.EffectiveFrom)
            .Select(t => new ManualLoanTerm { EffectiveFrom = t.EffectiveFrom, Principal = t.Principal, AprPercent = t.AprPercent }));
        loan.Collateral.Clear();
        loan.Collateral.AddRange(collateralRows.OrderBy(c => c.EffectiveFrom)
            .Select(c => new ManualLoanCollateral { EffectiveFrom = c.EffectiveFrom, Amount = c.Amount }));
    }

    private static string? ResolveKey(string? explicitKey, string symbol) =>
        !string.IsNullOrWhiteSpace(explicitKey) ? explicitKey.Trim().ToLowerInvariant() : AssetCatalog.PriceKeyFor(symbol);

    private static ManualLoanDto ToDto(ManualLoan l) => new(
        l.Id, l.Owner, l.Label, l.Platform, l.AssetSymbol, l.AssetPriceKey, l.CollateralSymbol, l.CollateralPriceKey,
        l.MarginCallLtvPercent, l.LiquidationLtvPercent, l.ClosedOn,
        l.Terms.OrderBy(t => t.EffectiveFrom).Select(t => new TermDto(t.EffectiveFrom, t.Principal, t.AprPercent)).ToList(),
        l.Collateral.OrderBy(c => c.EffectiveFrom).Select(c => new CollateralRowDto(c.EffectiveFrom, c.Amount)).ToList());
}
