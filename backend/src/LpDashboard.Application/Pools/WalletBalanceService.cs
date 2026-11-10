using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;

namespace LpDashboard.Application.Pools;

/// <param name="Raw">Balance in the token's smallest unit, as a string (it can exceed 2^53); null when the read failed.</param>
public sealed record TokenBalanceDto(string Token, string? Raw);

/// <param name="NativeRaw">The wallet's native-token balance in wei; the client treats it as WETH on chains where that is the pool token.</param>
public sealed record WalletBalancesDto(string Owner, Chain Chain, string? NativeRaw, IReadOnlyList<TokenBalanceDto> Balances);

public enum WalletBalanceFailure { NotWatched, NoProvider, InvalidToken, TooManyTokens }

public sealed class WalletBalanceException(WalletBalanceFailure failure, string message) : Exception(message)
{
    public WalletBalanceFailure Failure { get; } = failure;
}

/// <summary>
/// Reads a watched wallet's token balances over RPC, so the planner can start from what the wallet holds. Only wallets the
/// account follows are served, so the endpoint cannot be used to spend RPC quota on arbitrary addresses.
/// </summary>
public sealed class WalletBalanceService(IWatchedAddressRepository addresses, IEnumerable<IPositionProvider> providers)
{
    public const int MaxTokens = 8;

    public async Task<WalletBalancesDto> GetAsync(string owner, IReadOnlyCollection<string> tokens, Chain chain, CancellationToken ct)
    {
        if (tokens.Count > MaxTokens) throw new WalletBalanceException(WalletBalanceFailure.TooManyTokens, $"At most {MaxTokens} tokens at a time.");
        foreach (var t in tokens)
        {
            if (Addresses.AddressParser.TryParse(t) is not { Family: ChainFamily.Evm })
                throw new WalletBalanceException(WalletBalanceFailure.InvalidToken, $"{t} is not a token address.");
        }
        if (await addresses.FindAsync(owner, ct) is null)
            throw new WalletBalanceException(WalletBalanceFailure.NotWatched, "The wallet is not being watched.");

        var provider = providers.FirstOrDefault(p => p.Chain == chain)
            ?? throw new WalletBalanceException(WalletBalanceFailure.NoProvider, $"No chain provider is configured for {chain}.");

        var list = tokens.Select(t => t.Trim().ToLowerInvariant()).Distinct().ToList();
        var raw = list.Count == 0
            ? []
            : await provider.GetTokenBalancesAsync(list.Select(t => (owner, t)).ToList(), ct);
        var native = (await provider.GetNativeBalancesAsync([owner], ct))[0];

        return new WalletBalancesDto(
            owner, chain, native?.ToString(),
            list.Select((t, i) => new TokenBalanceDto(t, raw[i]?.ToString())).ToList());
    }
}
