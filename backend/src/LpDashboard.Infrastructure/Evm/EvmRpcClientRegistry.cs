using System.Collections.Concurrent;
using LpDashboard.Domain;
using Microsoft.Extensions.Options;

namespace LpDashboard.Infrastructure.Evm;

/// <summary>One shared <see cref="EvmRpcClient"/> per configured chain, so every reader of a chain shares the same request pacing.</summary>
internal sealed class EvmRpcClientRegistry(IHttpClientFactory httpClientFactory, IOptions<EvmOptions> options)
{
    private readonly ConcurrentDictionary<Chain, EvmRpcClient?> _clients = new();

    /// <summary>Null when the chain has no RPC URL configured.</summary>
    public EvmRpcClient? For(Chain chain) => _clients.GetOrAdd(chain, c =>
        options.Value.Chains.FirstOrDefault(x => x.Chain == c && !string.IsNullOrWhiteSpace(x.RpcUrl)) is { } chainOptions
            ? new EvmRpcClient(httpClientFactory, chainOptions)
            : null);
}
