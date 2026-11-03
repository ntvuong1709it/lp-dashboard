using LpDashboard.Application.Addresses;

namespace LpDashboard.Api;

/// <summary>
/// The <c>owner</c> query parameter: one or more comma-separated wallet addresses. It is required, so the API never
/// answers "everything that is watched" and a visitor only sees wallets they name.
/// </summary>
public static class OwnerParam
{
    public const int MaxOwners = 20;

    public static bool TryParse(string? raw, out IReadOnlyCollection<string> owners)
    {
        owners = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parsed = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (AddressParser.TryParse(part) is not { } address) return false;
            if (!parsed.Contains(address.Address)) parsed.Add(address.Address);
        }

        if (parsed.Count == 0 || parsed.Count > MaxOwners) return false;
        owners = parsed;
        return true;
    }
}
