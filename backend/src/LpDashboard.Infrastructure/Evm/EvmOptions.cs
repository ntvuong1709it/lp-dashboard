using LpDashboard.Domain;

namespace LpDashboard.Infrastructure.Evm;

public sealed class EvmOptions
{
    public const string SectionName = "Evm";

    public List<EvmChainOptions> Chains { get; set; } = [];
}

public sealed class EvmChainOptions
{
    public Chain Chain { get; set; }
    /// <summary>Keep the URL (contains the API key) in user-secrets, not appsettings.json.</summary>
    public string RpcUrl { get; set; } = string.Empty;
    /// <summary>Max eth_getLogs block range per request; 0 = try the full range and shrink on provider errors.</summary>
    public long MaxLogBlockRange { get; set; }
    /// <summary>Abort an eth_getLogs scan that would need more requests than this (e.g. Alchemy Free tier allows only 10 blocks per request).</summary>
    public int MaxLogRequestsPerScan { get; set; } = 200;
    /// <summary>Upper bound on JSON-RPC calls per second (each item of a batch counts); 0 = unlimited.</summary>
    public double MaxCallsPerSecond { get; set; } = 10;
    /// <summary>
    /// Use alchemy_getAssetTransfers for NFT discovery and to find the owner's transactions whose receipts hold the position events.
    /// Otherwise scan logs with eth_getLogs, which needs an RPC plan with a large eth_getLogs block range.
    /// </summary>
    public bool UseAlchemyTransfers { get; set; } = true;
    /// <summary>DefiLlama chain prefix, e.g. "base".</summary>
    public string PriceChainKey { get; set; } = string.Empty;
    public string NativePriceKey { get; set; } = "coingecko:ethereum";
    public List<EvmProtocolOptions> Protocols { get; set; } = [];
}

public sealed class EvmProtocolOptions
{
    public Protocol Protocol { get; set; }
    public bool Enabled { get; set; } = true;
    public List<EvmDeploymentOptions> Deployments { get; set; } = [];
}

/// <summary>One NonfungiblePositionManager + its pool factory (Aerodrome has several on Base).</summary>
public sealed class EvmDeploymentOptions
{
    public string PositionManager { get; set; } = string.Empty;
    public string Factory { get; set; } = string.Empty;
    /// <summary>Only used for log-based discovery when UseAlchemyTransfers is false.</summary>
    public long StartBlock { get; set; }
    /// <summary>Emission token of this deployment's gauges when they lack rewardToken() (Slipstream V3); empty = ask the gauge.</summary>
    public string? GaugeRewardToken { get; set; }
}
