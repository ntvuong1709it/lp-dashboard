using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Application.Pools;
using LpDashboard.Application.Queries;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Risk;

/// <summary>Caps how many token checks run at once, across requests and the nightly job.</summary>
public sealed class RiskCheckGate(IOptions<RiskSettings> settings)
{
    private readonly SemaphoreSlim _gate = new(Math.Max(1, settings.Value.MaxConcurrentChecks));

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}

/// <summary>A check that was just run and stored, with what changed against the one before it.</summary>
public sealed record TokenCheckResult(TokenRiskAssessment Assessment, RiskComparison Comparison, DateTime CheckedAt);

public sealed record PoolCheckResult(PoolRiskAssessment Assessment, RiskComparison Comparison, DateTime CheckedAt);

/// <summary>
/// Runs, stores and serves risk checks. Reading a check never calls a source when a recent one exists; listing pools or
/// positions (the <c>Summaries</c> methods) only reads the cache.
/// </summary>
public sealed class RiskService(
    RiskFactCollector collector,
    RiskCheckGate gate,
    IRiskRepository repository,
    IUnitOfWork unitOfWork,
    IPositionRepository positions,
    PoolExplorerService explorer,
    IOptions<RiskOptions> options,
    IOptions<RiskSettings> settings,
    TimeProvider time,
    ILogger<RiskService> logger)
{
    private const int TopFindings = 3;

    // ---- single token

    public async Task<TokenRiskDto> GetTokenAsync(Chain chain, string address, bool refresh, CancellationToken ct)
    {
        var token = Normalize(address);
        var latest = await repository.GetLatestAsync(RiskSubjectKind.Token, chain, token, ct);
        var now = time.GetUtcNow().UtcDateTime;

        if (latest is not null)
        {
            var age = now - latest.CheckedAt;
            var cooldown = TimeSpan.FromMinutes(settings.Value.RefreshCooldownMinutes);
            if (refresh && age < cooldown) throw new RiskCooldownException(cooldown - age);
            if (!refresh && age < TimeSpan.FromHours(settings.Value.CacheHours)) return ToDto(chain, latest, now);
        }

        var result = await CheckTokenAsync(chain, token, ct);
        return ToDto(chain, token, result.Assessment, result.CheckedAt, now);
    }

    /// <summary>Collects facts, evaluates, stores the check and prunes old ones. Always calls the sources.</summary>
    public async Task<TokenCheckResult> CheckTokenAsync(Chain chain, string address, CancellationToken ct, string? knownSymbol = null)
    {
        var token = Normalize(address);
        TokenRiskFacts facts;
        using (await gate.EnterAsync(ct))
        {
            facts = await collector.CollectTokenAsync(chain, token, knownSymbol, ct);
        }

        if (facts.Sources.Any(s => s.Name == RiskSources.Rpc && s.Detail == OnChainReading.NoContract))
            throw new RiskInputException("There is no contract at this address, so it is not a token.");

        var assessment = RiskEvaluator.EvaluateToken(facts, options.Value);
        var previousRow = await repository.GetLatestAsync(RiskSubjectKind.Token, chain, token, ct);
        var previous = previousRow is null ? null : ReevaluateToken(previousRow);
        var comparison = RiskChangeDetector.Compare(previous, assessment);

        var row = new RiskCheck
        {
            Kind = RiskSubjectKind.Token,
            Chain = chain,
            Subject = token,
            CheckedAt = facts.CollectedAt,
            Level = assessment.Verdict.Level,
            ChecksRun = assessment.Verdict.ChecksRun,
            ChecksTotal = assessment.Verdict.ChecksTotal,
            FindingsJson = RiskJson.Serialize(assessment.Findings),
            FactsJson = RiskJson.Serialize(facts),
            SourcesJson = RiskJson.Serialize(facts.Sources),
            ChangesJson = RiskJson.Serialize(comparison.Changes),
        };
        await repository.AddCheckAsync(row, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await repository.PruneChecksAsync(RiskSubjectKind.Token, chain, token, settings.Value.HistoryKeep, ct);
        return new TokenCheckResult(assessment, comparison, facts.CollectedAt);
    }

    // ---- pools

    public async Task<PoolRiskDto> GetPoolAsync(string poolId, bool refresh, CancellationToken ct)
    {
        var state = await explorer.ResolvePoolStateAsync(poolId, ct);
        var chain = Chain.Base;
        var pool = state.PoolAddress.ToLowerInvariant();
        var now = time.GetUtcNow().UtcDateTime;

        // Both tokens first: a refresh is subject to the token cooldown, a cache hit costs nothing.
        var token0 = await TokenAssessmentAsync(chain, state.Token0, refresh, ct);
        var token1 = await TokenAssessmentAsync(chain, state.Token1, refresh, ct);

        var latest = await repository.GetLatestAsync(RiskSubjectKind.Pool, chain, pool, ct);
        PoolRiskAssessment assessment;
        DateTime checkedAt;
        if (latest is not null && !refresh && now - latest.CheckedAt < TimeSpan.FromHours(settings.Value.CacheHours)
            && ReevaluatePool(latest, token0.Assessment, token1.Assessment) is { } cached)
        {
            assessment = cached;
            checkedAt = latest.CheckedAt;
        }
        else
        {
            var result = await CheckPoolAsync(poolId, state, token0.Assessment, token1.Assessment, ct);
            assessment = result.Assessment;
            checkedAt = result.CheckedAt;
        }

        var oldest = new[] { checkedAt, token0.CheckedAt, token1.CheckedAt }.Min();
        return new PoolRiskDto(
            poolId, pool, assessment.Verdict.Level, assessment.Verdict.ChecksRun, assessment.Verdict.ChecksTotal, oldest,
            assessment.Findings.Select(f => ToDto(chain, f)).ToList(),
            assessment.Facts.Sources.Select(ToDto).ToList(),
            ToDto(chain, token0.Address, token0.Assessment, token0.CheckedAt, now),
            ToDto(chain, token1.Address, token1.Assessment, token1.CheckedAt, now),
            RiskLinks.ForPool(chain, pool),
            RecheckAt(checkedAt, now));
    }

    /// <summary>Runs and stores the pool's own checks (age, emissions, gauge). Token checks are separate rows.</summary>
    public async Task<PoolCheckResult> CheckPoolAsync(
        string poolId, PoolStateDto state, TokenRiskAssessment token0, TokenRiskAssessment token1, CancellationToken ct)
    {
        var chain = Chain.Base;
        var pool = state.PoolAddress.ToLowerInvariant();
        var listed = explorer.FindListed(poolId);
        var facts = await collector.CollectPoolAsync(
            chain, pool, hasGauge: state.Protocol == Protocol.AerodromeSlipstream, listed?.FeeApr7d ?? listed?.FeeApr1d, listed?.RewardApr, ct);

        var assessment = RiskEvaluator.EvaluatePool(facts, token0, token1, options.Value);
        var previousRow = await repository.GetLatestAsync(RiskSubjectKind.Pool, chain, pool, ct);
        var comparison = RiskChangeDetector.ComparePool(previousRow is null ? null : ReevaluatePool(previousRow, token0, token1), assessment);

        var row = new RiskCheck
        {
            Kind = RiskSubjectKind.Pool,
            Chain = chain,
            Subject = pool,
            ExplorerId = poolId,
            CheckedAt = facts.CollectedAt,
            // The pool's own verdict; the combined one with its tokens is worked out when read, since tokens refresh on their own.
            Level = assessment.OwnVerdict.Level,
            ChecksRun = assessment.OwnVerdict.ChecksRun,
            ChecksTotal = assessment.OwnVerdict.ChecksTotal,
            FindingsJson = RiskJson.Serialize(assessment.Findings),
            FactsJson = RiskJson.Serialize(facts),
            SourcesJson = RiskJson.Serialize(facts.Sources),
            ChangesJson = RiskJson.Serialize(comparison.Changes),
        };
        await repository.AddCheckAsync(row, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await repository.PruneChecksAsync(RiskSubjectKind.Pool, chain, pool, settings.Value.HistoryKeep, ct);
        return new PoolCheckResult(assessment, comparison, facts.CollectedAt);
    }

    private async Task<(string Address, TokenRiskAssessment Assessment, DateTime CheckedAt)> TokenAssessmentAsync(
        Chain chain, TokenInfo token, bool refresh, CancellationToken ct)
    {
        var address = token.Address.ToLowerInvariant();
        var latest = await repository.GetLatestAsync(RiskSubjectKind.Token, chain, address, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (latest is not null)
        {
            var age = now - latest.CheckedAt;
            // A refresh of a pool re-checks its tokens, but a token checked a moment ago is not asked again.
            var stale = refresh ? age >= TimeSpan.FromMinutes(settings.Value.RefreshCooldownMinutes) : age >= TimeSpan.FromHours(settings.Value.CacheHours);
            if (!stale && ReevaluateToken(latest) is { } cached) return (address, cached, latest.CheckedAt);
        }

        var result = await CheckTokenAsync(chain, address, ct, token.Symbol);
        return (address, result.Assessment, result.CheckedAt);
    }

    // ---- cache-only reads for lists

    /// <summary>Cached verdicts of these tokens; tokens with no check are absent.</summary>
    public async Task<IReadOnlyDictionary<string, RiskSummaryDto>> TokenSummariesAsync(Chain chain, IReadOnlyCollection<string> addresses, CancellationToken ct)
    {
        var rows = await repository.ListLatestAsync(RiskSubjectKind.Token, chain, addresses.Select(a => a.ToLowerInvariant()).Distinct().ToList(), ct);
        return rows.ToDictionary(r => r.Subject, Summarize, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The verdict of each pool in the explorer's list: the worst of its two tokens' cached verdicts and, once the pool has
    /// been opened, its own check. Pools with nothing cached are absent.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, RiskSummaryDto>> PoolSummariesAsync(
        Chain chain, IReadOnlyCollection<PoolDto> pools, CancellationToken ct)
    {
        var tokens = await TokenSummariesAsync(chain, pools.SelectMany(p => new[] { p.Token0, p.Token1 }).ToList(), ct);
        var poolRows = (await repository.ListLatestPoolsByExplorerIdAsync(chain, pools.Select(p => p.Id).ToList(), ct))
            .ToDictionary(r => r.ExplorerId!, StringComparer.Ordinal);

        var result = new Dictionary<string, RiskSummaryDto>(StringComparer.Ordinal);
        foreach (var p in pools)
        {
            tokens.TryGetValue(p.Token0.ToLowerInvariant(), out var t0);
            tokens.TryGetValue(p.Token1.ToLowerInvariant(), out var t1);
            poolRows.TryGetValue(p.Id, out var own);
            if (Combine(own is null ? null : Summarize(own), t0, t1) is { } summary) result[p.Id] = summary;
        }
        return result;
    }

    /// <summary>The same for the pools of open positions, keyed by pool address.</summary>
    public async Task<IReadOnlyDictionary<string, RiskSummaryDto>> PoolSummariesByAddressAsync(
        Chain chain, IReadOnlyCollection<(string Pool, string Token0, string Token1)> pools, CancellationToken ct)
    {
        var tokens = await TokenSummariesAsync(chain, pools.SelectMany(p => new[] { p.Token0, p.Token1 }).ToList(), ct);
        var poolRows = (await repository.ListLatestAsync(RiskSubjectKind.Pool, chain, pools.Select(p => p.Pool.ToLowerInvariant()).Distinct().ToList(), ct))
            .ToDictionary(r => r.Subject, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, RiskSummaryDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pool, token0, token1) in pools)
        {
            tokens.TryGetValue(token0.ToLowerInvariant(), out var t0);
            tokens.TryGetValue(token1.ToLowerInvariant(), out var t1);
            poolRows.TryGetValue(pool.ToLowerInvariant(), out var own);
            if (Combine(own is null ? null : Summarize(own), t0, t1) is { } summary) result[pool] = summary;
        }
        return result;
    }

    // ---- Risk page

    /// <summary>Tokens of the owners' open positions and of starred pools, each with its cached verdict.</summary>
    public async Task<IReadOnlyList<RiskHeldRowDto>> GetHeldAsync(IReadOnlyCollection<string> owners, CancellationToken ct)
    {
        var scope = await ScopeAsync(owners, ct);
        var summaries = await TokenSummariesAsync(Chain.Base, scope.Tokens.Keys.ToList(), ct);
        return scope.Tokens
            .Select(kv => new RiskHeldRowDto(kv.Key, kv.Value.Symbol, kv.Value.Reasons.Order().ToList(), summaries.GetValueOrDefault(kv.Key)))
            .OrderBy(r => RankForSort(r.Risk?.Level))
            .ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Recent changes (a finding appeared or cleared, the level moved) across held and starred tokens and pools.</summary>
    public async Task<IReadOnlyList<RiskChangeDto>> GetRecentAsync(IReadOnlyCollection<string> owners, int take, CancellationToken ct)
    {
        var scope = await ScopeAsync(owners, ct);
        var subjects = scope.Tokens.Keys.Concat(scope.Pools.Keys).ToList();
        if (subjects.Count == 0) return [];

        var rows = await repository.ListRecentChangesAsync(Chain.Base, subjects, Math.Clamp(take, 1, 200), ct);
        var result = new List<RiskChangeDto>();
        foreach (var row in rows)
        {
            var symbol = row.Kind == RiskSubjectKind.Token
                ? scope.Tokens.GetValueOrDefault(row.Subject)?.Symbol
                : scope.Pools.GetValueOrDefault(row.Subject);
            foreach (var change in RiskJson.TryDeserialize<List<RiskChange>>(row.ChangesJson) ?? [])
                result.Add(new RiskChangeDto(row.Kind, row.Subject, symbol, row.CheckedAt, row.Level, change.Description, change.IsWorse));
        }
        return result.OrderByDescending(c => c.At).Take(take).ToList();
    }

    /// <summary>Everything the owner holds or follows that the nightly job re-checks.</summary>
    public async Task<RiskScope> ScopeAsync(IReadOnlyCollection<string> owners, CancellationToken ct)
    {
        var tokens = new Dictionary<string, (string Symbol, HashSet<string> Reasons)>(StringComparer.OrdinalIgnoreCase);
        var pools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddToken(string address, string symbol, string reason)
        {
            var key = address.ToLowerInvariant();
            if (!tokens.TryGetValue(key, out var entry)) tokens[key] = entry = (symbol, []);
            entry.Reasons.Add(reason);
        }

        if (owners.Count > 0)
        {
            foreach (var position in await positions.QueryAsync(new PositionFilter(owners, PositionStatus.Open), ct))
            {
                if (position.Pool is not { } pool) continue;
                AddToken(pool.Token0.Address, pool.Token0.Symbol, "held");
                AddToken(pool.Token1.Address, pool.Token1.Symbol, "held");
                pools[pool.Address.ToLowerInvariant()] = $"{pool.Token0.Symbol}/{pool.Token1.Symbol}";
            }
        }

        foreach (var star in await repository.ListStarsAsync(Chain.Base, ct))
        {
            var symbols = star.Symbol.Split(['/', '-'], 2);
            AddToken(star.Token0, symbols.Length > 0 ? symbols[0].Trim() : "?", "starred");
            AddToken(star.Token1, symbols.Length > 1 ? symbols[1].Trim() : "?", "starred");
            if (star.PoolAddress is not null) pools[star.PoolAddress.ToLowerInvariant()] = star.Symbol;
        }

        return new RiskScope(
            tokens.ToDictionary(kv => kv.Key, kv => new RiskScopeToken(kv.Value.Symbol, kv.Value.Reasons.ToList()), StringComparer.OrdinalIgnoreCase),
            pools);
    }

    // ---- stars

    public async Task<IReadOnlySet<string>> StarredIdsAsync(CancellationToken ct) =>
        (await repository.ListStarsAsync(Chain.Base, ct)).Select(s => s.ExplorerId).ToHashSet(StringComparer.Ordinal);

    public async Task StarAsync(string poolId, CancellationToken ct)
    {
        if (await repository.FindStarAsync(Chain.Base, poolId, ct) is not null) return;
        var listed = explorer.FindListed(poolId)
            ?? throw new EstimateException(EstimateFailure.PoolNotFound, "Pool not found. Reload the pool list and try again.");
        repository.Add(new StarredPool
        {
            Chain = listed.Chain,
            ExplorerId = poolId,
            Symbol = listed.Symbol,
            Token0 = listed.Token0.ToLowerInvariant(),
            Token1 = listed.Token1.ToLowerInvariant(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task UnstarAsync(string poolId, CancellationToken ct)
    {
        if (await repository.FindStarAsync(Chain.Base, poolId, ct) is not { } star) return;
        repository.Remove(star);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // ---- decoration

    public async Task<PoolListDto> DecoratePoolsAsync(PoolListDto list, CancellationToken ct)
    {
        try
        {
            var summaries = await PoolSummariesAsync(Chain.Base, list.Pools, ct);
            var starred = await StarredIdsAsync(ct);
            return list with
            {
                Pools = list.Pools.Select(p => p with { Risk = summaries.GetValueOrDefault(p.Id), Starred = starred.Contains(p.Id) }).ToList(),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The risk columns are an extra: a failure here must never take the pool list down.
            logger.LogWarning("Could not attach risk verdicts to the pool list: {Type}", ex.GetType().Name);
            return list;
        }
    }

    /// <summary>Attaches the cached verdict of each position's pool and tokens. A failure here never fails the position list.</summary>
    public async Task<IReadOnlyList<PositionDto>> DecoratePositionsAsync(IReadOnlyList<PositionDto> list, CancellationToken ct)
    {
        try
        {
            var result = list.ToList();
            foreach (var byChain in list.Where(p => p is { PoolAddress: not null, Token0: not null, Token1: not null }).GroupBy(p => p.Chain))
            {
                var pools = byChain.Select(p => (p.PoolAddress!, p.Token0!.Address, p.Token1!.Address)).Distinct().ToList();
                var summaries = await PoolSummariesByAddressAsync(byChain.Key, pools, ct);
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].Chain == byChain.Key && result[i].PoolAddress is { } pool && summaries.TryGetValue(pool, out var s))
                        result[i] = result[i] with { Risk = s };
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not attach risk verdicts to the positions: {Type}", ex.GetType().Name);
            return list;
        }
    }

    // ---- helpers

    internal TokenRiskAssessment? ReevaluateToken(RiskCheck row) =>
        RiskJson.TryDeserialize<TokenRiskFacts>(row.FactsJson) is { } facts ? RiskEvaluator.EvaluateToken(facts, options.Value) : null;

    internal PoolRiskAssessment? ReevaluatePool(RiskCheck row, TokenRiskAssessment? token0, TokenRiskAssessment? token1) =>
        RiskJson.TryDeserialize<PoolRiskFacts>(row.FactsJson) is { } facts ? RiskEvaluator.EvaluatePool(facts, token0, token1, options.Value) : null;

    private TokenRiskDto ToDto(Chain chain, RiskCheck row, DateTime now)
    {
        var facts = RiskJson.TryDeserialize<TokenRiskFacts>(row.FactsJson);
        if (facts is not null)
            return ToDto(chain, row.Subject, RiskEvaluator.EvaluateToken(facts, options.Value), row.CheckedAt, now);

        // Facts that no longer deserialize (a format change): fall back to what was stored.
        var findings = RiskJson.TryDeserialize<List<RiskFinding>>(row.FindingsJson) ?? [];
        var sources = RiskJson.TryDeserialize<List<SourceResult>>(row.SourcesJson) ?? [];
        return new TokenRiskDto(
            chain, row.Subject, null, row.Level, row.ChecksRun, row.ChecksTotal, row.CheckedAt, false,
            findings.Select(f => ToDto(chain, f)).ToList(), sources.Select(ToDto).ToList(), RiskLinks.ForToken(chain, row.Subject), RecheckAt(row.CheckedAt, now));
    }

    private TokenRiskDto ToDto(Chain chain, string address, TokenRiskAssessment a, DateTime checkedAt, DateTime now) => new(
        chain, address, a.Facts.Symbol, a.Verdict.Level, a.Verdict.ChecksRun, a.Verdict.ChecksTotal, checkedAt, a.Trusted,
        a.Findings.OrderByDescending(f => f.Severity).Select(f => ToDto(chain, f)).ToList(),
        a.Facts.Sources.Select(ToDto).ToList(), RiskLinks.ForToken(chain, address), RecheckAt(checkedAt, now));

    private static RiskFindingDto ToDto(Chain chain, RiskFinding f) =>
        new(f.Code, f.Severity, f.Title, f.Evidence, f.Source, f.Subject, RiskLinks.ExplorerAddress(chain, f.Subject));

    private static RiskSourceDto ToDto(SourceResult s) => new(s.Name, RiskSources.DisplayName(s.Name), s.Required, s.Answered, s.Detail);

    private DateTime? RecheckAt(DateTime checkedAt, DateTime now)
    {
        var at = checkedAt + TimeSpan.FromMinutes(settings.Value.RefreshCooldownMinutes);
        return at > now ? at : null;
    }

    private static RiskSummaryDto Summarize(RiskCheck row)
    {
        var top = (RiskJson.TryDeserialize<List<RiskFinding>>(row.FindingsJson) ?? [])
            .Where(f => f.Severity != RiskSeverity.Info)
            .OrderByDescending(f => f.Severity)
            .Take(TopFindings)
            .Select(f => f.Title)
            .ToList();
        return new RiskSummaryDto(row.Level, row.ChecksRun, row.ChecksTotal, row.CheckedAt, top);
    }

    /// <summary>
    /// Worst-of over the cached parts that exist. With one of the two tokens never checked, only a Red or Yellow finding is
    /// worth showing: a clean result would be half a verdict, so the row reads "not checked" (null) instead.
    /// </summary>
    private static RiskSummaryDto? Combine(RiskSummaryDto? own, RiskSummaryDto? t0, RiskSummaryDto? t1)
    {
        if (own is null && t0 is null && t1 is null) return null;
        var parts = new List<RiskVerdict>();
        if (own is not null) parts.Add(new RiskVerdict(own.Level, own.ChecksRun, own.ChecksTotal));
        parts.Add(t0 is null ? RiskVerdict.NotChecked : new RiskVerdict(t0.Level, t0.ChecksRun, t0.ChecksTotal));
        parts.Add(t1 is null ? RiskVerdict.NotChecked : new RiskVerdict(t1.Level, t1.ChecksRun, t1.ChecksTotal));
        var verdict = RiskVerdict.Combine([.. parts]);
        if ((t0 is null || t1 is null) && verdict.Level is not (RiskLevel.Red or RiskLevel.Yellow)) return null;

        var top = new[] { own, t0, t1 }.Where(s => s is not null).SelectMany(s => s!.Top).Distinct().Take(TopFindings).ToList();
        var checkedAt = new[] { own, t0, t1 }.Where(s => s?.CheckedAt is not null).Select(s => s!.CheckedAt!.Value).DefaultIfEmpty().Min();
        return new RiskSummaryDto(verdict.Level, verdict.ChecksRun, verdict.ChecksTotal, checkedAt == default ? null : checkedAt, top);
    }

    private static int RankForSort(RiskLevel? level) => level switch
    {
        RiskLevel.Red => 0,
        RiskLevel.Yellow => 1,
        RiskLevel.Partial => 2,
        null or RiskLevel.NotChecked => 3,
        _ => 4,
    };

    private static string Normalize(string address) =>
        AddressParser.TryParse(address) is { Family: ChainFamily.Evm } parsed
            ? parsed.Address
            : throw new RiskInputException("Enter a token contract address: 0x followed by 40 hex characters.");
}

public sealed record RiskScopeToken(string Symbol, IReadOnlyList<string> Reasons);

/// <summary>Tokens (address to symbol and reasons) and pools (address to symbol) in the owner's risk scope.</summary>
public sealed record RiskScope(IReadOnlyDictionary<string, RiskScopeToken> Tokens, IReadOnlyDictionary<string, string> Pools);
