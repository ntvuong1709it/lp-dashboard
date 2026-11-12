using System.Numerics;
using LpDashboard.Domain.Risk;

namespace LpDashboard.Domain.Entities;

public enum RiskSubjectKind
{
    Token,
    Pool,
}

/// <summary>
/// One risk check of a token or a pool. The latest row per subject (<see cref="IsLatest"/>) is the current verdict; the last
/// few are kept for change detection. <see cref="FactsJson"/> holds the raw numbers so a rule change can re-evaluate them.
/// </summary>
public class RiskCheck
{
    public int Id { get; set; }
    public RiskSubjectKind Kind { get; set; }
    public Chain Chain { get; set; }

    /// <summary>Token address, or pool contract address; lower case.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>For pools: the pool explorer's id, so the Pools list can find the check without an RPC call.</summary>
    public string? ExplorerId { get; set; }

    public DateTime CheckedAt { get; set; }
    public bool IsLatest { get; set; }
    public RiskLevel Level { get; set; }
    public int ChecksRun { get; set; }
    public int ChecksTotal { get; set; }
    public string FindingsJson { get; set; } = "[]";
    public string FactsJson { get; set; } = "{}";
    public string SourcesJson { get; set; } = "[]";

    /// <summary>What changed against the previous check (appeared, cleared, worse), for the recent-changes list.</summary>
    public string ChangesJson { get; set; } = "[]";
}

/// <summary>A pool the owner follows. Part of the nightly re-check scope.</summary>
public class StarredPool
{
    public int Id { get; set; }
    public Chain Chain { get; set; }

    /// <summary>The pool explorer's id (it has no contract address).</summary>
    public string ExplorerId { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;
    public string Token0 { get; set; } = string.Empty;
    public string Token1 { get; set; } = string.Empty;

    /// <summary>Filled in the first time the pool is resolved on-chain.</summary>
    public string? PoolAddress { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Raw token balances of a pool at a time. Raw amounts, not USD, so a price move never looks like liquidity leaving.</summary>
public class PoolLiquiditySnapshot
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public string PoolAddress { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public BigInteger Token0Raw { get; set; }
    public BigInteger Token1Raw { get; set; }
}

/// <summary>De-duplication record: one alert per (subject, code) until it clears.</summary>
public class RiskAlertState
{
    public int Id { get; set; }
    public RiskSubjectKind Kind { get; set; }
    public Chain Chain { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime FirstAlertedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
}
