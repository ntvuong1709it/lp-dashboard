using System.Text.Json;
using System.Text.Json.Serialization;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;

namespace LpDashboard.Application.Risk;

public static class RiskJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <param name="Link">Where to look the evidence up (a Basescan address page), when the finding names an address.</param>
public sealed record RiskFindingDto(string Code, RiskSeverity Severity, string Title, string Evidence, string Source, string? Subject, string? Link);

public sealed record RiskSourceDto(string Name, string DisplayName, bool Required, bool Answered, string? Detail);

/// <summary>Links to the pages behind a report. Null where the page does not exist for this subject.</summary>
public sealed record RiskLinksDto(string? Explorer, string? Honeypot, string? DexScreener);

/// <summary>The verdict in the smallest form a table row needs. Read from the cache only.</summary>
/// <param name="Top">Titles of the worst findings, for the badge tooltip.</param>
public sealed record RiskSummaryDto(RiskLevel Level, int ChecksRun, int ChecksTotal, DateTime? CheckedAt, IReadOnlyList<string> Top);

public sealed record TokenRiskDto(
    Chain Chain,
    string Address,
    string? Symbol,
    RiskLevel Level,
    int ChecksRun,
    int ChecksTotal,
    DateTime CheckedAt,
    bool Trusted,
    IReadOnlyList<RiskFindingDto> Findings,
    IReadOnlyList<RiskSourceDto> Sources,
    RiskLinksDto Links,
    /// <summary>When the next forced re-check is allowed (the cooldown after this check); null when it already is.</summary>
    DateTime? RecheckAvailableAt);

public sealed record PoolRiskDto(
    string PoolId,
    string PoolAddress,
    RiskLevel Level,
    int ChecksRun,
    int ChecksTotal,
    DateTime CheckedAt,
    IReadOnlyList<RiskFindingDto> Findings,
    IReadOnlyList<RiskSourceDto> Sources,
    TokenRiskDto Token0,
    TokenRiskDto Token1,
    RiskLinksDto Links,
    DateTime? RecheckAvailableAt);

/// <summary>One line of the recent-changes list on the Risk page.</summary>
public sealed record RiskChangeDto(
    RiskSubjectKind Kind, string Subject, string? Symbol, DateTime At, RiskLevel Level, string Description, bool IsWorse);

/// <summary>A row of the Risk page's table: a token the owner holds or follows, with its cached verdict.</summary>
/// <param name="Reason">"held" (an open position uses it), "starred" (a starred pool uses it) or both.</param>
public sealed record RiskHeldRowDto(string Address, string Symbol, IReadOnlyList<string> Reasons, RiskSummaryDto? Risk);

public sealed class RiskCooldownException(TimeSpan retryAfter) : Exception("This token was checked a moment ago.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>The input is not something that can be checked (bad address, no contract at the address).</summary>
public sealed class RiskInputException(string message) : Exception(message);

public static class RiskLinks
{
    public static string? ExplorerAddress(Chain chain, string? address) =>
        chain == Chain.Base && !string.IsNullOrWhiteSpace(address) ? $"https://basescan.org/address/{address}" : null;

    public static RiskLinksDto ForToken(Chain chain, string token) => chain == Chain.Base
        ? new RiskLinksDto(ExplorerAddress(chain, token), $"https://honeypot.is/base?address={token}", $"https://dexscreener.com/base/{token}")
        : new RiskLinksDto(null, null, null);

    public static RiskLinksDto ForPool(Chain chain, string pool) => chain == Chain.Base
        ? new RiskLinksDto(ExplorerAddress(chain, pool), null, $"https://dexscreener.com/base/{pool}")
        : new RiskLinksDto(null, null, null);
}
