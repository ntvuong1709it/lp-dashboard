using System.Text.RegularExpressions;
using LpDashboard.Domain;

namespace LpDashboard.Application.Addresses;

public static partial class AddressParser
{
    [GeneratedRegex("^0x[0-9a-fA-F]{40}$")]
    private static partial Regex EvmAddress();

    [GeneratedRegex("^[1-9A-HJ-NP-Za-km-z]{32,44}$")]
    private static partial Regex SolanaAddress();

    /// <summary>Returns the normalized address and its chain family, or null when invalid.</summary>
    public static (string Address, ChainFamily Family)? TryParse(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (EvmAddress().IsMatch(value)) return (value.ToLowerInvariant(), ChainFamily.Evm);
        // Solana addresses are case-sensitive.
        if (SolanaAddress().IsMatch(value)) return (value, ChainFamily.Solana);
        return null;
    }
}
