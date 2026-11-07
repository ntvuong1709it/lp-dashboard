using System.Numerics;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Infrastructure.Evm;

/// <summary>
/// Reads Aave v3 for a wallet over JSON-RPC: the account (health factor, collateral, debt), each reserve it touches, and
/// end-of-day balances from historical state. Read-only. The pool and data provider are looked up from the market's
/// PoolAddressesProvider once.
/// </summary>
internal sealed class AaveV3Provider(EvmRpcClient rpc, EvmChainOptions chain, AaveV3Options options, ILogger<AaveV3Provider> logger)
    : ILendingProvider
{
    private const int BaseDecimals = 8;
    /// <summary>Share of a history read's progress bar given to finding each day's block; the rest is reading balances.</summary>
    private const double BlockSearchShare = 0.25;

    private static readonly string SupplyTopic = Abi.Topic("Supply(address,address,address,uint256,uint16)");
    private static readonly string BorrowTopic = Abi.Topic("Borrow(address,address,address,uint256,uint8,uint256,uint16)");

    private readonly SemaphoreSlim _resolve = new(1, 1);
    private string? _pool;
    private string? _dataProvider;
    private IReadOnlyList<string>? _reserves;
    private IReadOnlyDictionary<string, string>? _tokens;
    private readonly Dictionary<string, (string Symbol, int Decimals)> _meta = new(StringComparer.OrdinalIgnoreCase);

    public Chain Chain => chain.Chain;

    public string Market => "Aave v3";

    private string PriceKey(string reserve) => $"{chain.PriceChainKey}:{reserve.ToLowerInvariant()}";

    // ---- live ------------------------------------------------------------------------------------------------------

    public async Task<LendingAccountState?> GetAccountAsync(string owner, CancellationToken ct)
    {
        await ResolveAsync(ct);
        var latest = await rpc.GetBlockNumberAsync(ct);

        var accountWords = Abi.Words(await rpc.EthCallAsync(_pool!, Abi.Encode("getUserAccountData(address)", Abi.Word(owner)), ct));
        if (accountWords.Count < 6) return null;

        var collateral = TokenAmount.FromRaw(Abi.ToUInt(accountWords[0]), BaseDecimals);
        var debt = TokenAmount.FromRaw(Abi.ToUInt(accountWords[1]), BaseDecimals);
        var threshold = (decimal)(long)Abi.ToUInt(accountWords[3]) / 10000m;
        decimal? healthFactor = debt > 0 ? TokenAmount.FromRaw(Abi.ToUInt(accountWords[5]), 18) : null;

        var reserves = await ListReservesAsync(ct);
        var calls = reserves.Select(r => EvmRpcClient.EthCall(_dataProvider!, UserReserveData(r, owner))).ToList();
        var results = await rpc.BatchAsync(calls, ct);

        var held = new List<(string Reserve, IReadOnlyList<string> Words)>();
        for (var i = 0; i < reserves.Count; i++)
        {
            if (results[i].Error is { } error) throw error;
            var words = Abi.Words(results[i].Result.GetString());
            if (words.Count >= 9 && (!Abi.ToUInt(words[0]).IsZero || !Abi.ToUInt(words[1]).IsZero || !Abi.ToUInt(words[2]).IsZero))
                held.Add((reserves[i], words));
        }

        var meta = await MetaAsync(held.Select(h => h.Reserve), ct);
        var extra = new List<RpcCall>();
        foreach (var (reserve, _) in held)
        {
            extra.Add(EvmRpcClient.EthCall(_pool!, Abi.Encode("getReserveNormalizedIncome(address)", Abi.Word(reserve))));
            extra.Add(EvmRpcClient.EthCall(_pool!, Abi.Encode("getReserveNormalizedVariableDebt(address)", Abi.Word(reserve))));
            extra.Add(EvmRpcClient.EthCall(_dataProvider!, Abi.Encode("getReserveData(address)", Abi.Word(reserve))));
        }
        var extraResults = held.Count == 0 ? [] : await rpc.BatchAsync(extra, ct);

        var states = new List<LendingReserveState>();
        for (var i = 0; i < held.Count; i++)
        {
            var (reserve, words) = held[i];
            var (symbol, decimals) = meta[reserve];
            for (var k = 0; k < 3; k++)
            {
                if (extraResults[i * 3 + k].Error is { } error) throw error;
            }
            var income = Abi.ToUInt(extraResults[i * 3].Result.GetString()!);
            var debtIndex = Abi.ToUInt(extraResults[i * 3 + 1].Result.GetString()!);
            var data = Abi.Words(extraResults[i * 3 + 2].Result.GetString());

            states.Add(new LendingReserveState(
                reserve, symbol, decimals, PriceKey(reserve),
                TokenAmount.FromRaw(Abi.ToUInt(words[0]), decimals),
                TokenAmount.FromRaw(Abi.ToUInt(words[1]) + Abi.ToUInt(words[2]), decimals),
                data.Count > 6 ? AaveAccrual.RayRateToPercent(Abi.ToUInt(data[5])) : 0,
                data.Count > 6 ? AaveAccrual.RayRateToPercent(Abi.ToUInt(data[6])) : 0,
                TokenAmount.FromRaw(income, 27),
                TokenAmount.FromRaw(debtIndex, 27),
                Abi.ToUInt(words[8]) != 0));
        }

        return new LendingAccountState(latest, collateral, debt, threshold, healthFactor, states);
    }

    // ---- first activity --------------------------------------------------------------------------------------------

    public async Task<LendingActivity?> FindActivityAsync(string owner, CancellationToken ct)
    {
        await ResolveAsync(ct);

        // Plans that cap eth_getLogs at a few blocks (Alchemy free tier: 10) cannot scan the market's events, so ask the
        // indexed transfer history first: a supply mints aTokens to the wallet and a borrow mints debt tokens to it.
        if (chain.UseAlchemyTransfers)
        {
            try
            {
                return await FindActivityByTransfersAsync(owner, ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or RpcException or HttpRequestException)
            {
                logger.LogInformation(ex, "Transfer history is unavailable; scanning Aave events for the wallet's first activity instead");
            }
        }

        try
        {
            var latest = await rpc.GetBlockNumberAsync(ct);
            var logs = await rpc.GetLogsAsync(
                _pool!, [new[] { SupplyTopic, BorrowTopic }, null, Abi.TopicOf(owner)], Math.Max(0, options.StartBlock), latest, ct);
            if (logs.Count == 0) return null;

            var first = logs.Min(l => l.BlockNumber);
            var stamps = await rpc.GetBlockTimestampsAsync([first], ct);
            var reserves = logs.Select(l => Abi.ToAddress(l.Topics[1])).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new LendingActivity(DateOnly.FromDateTime(stamps[first]), reserves);
        }
        catch (Exception ex) when (ex is InvalidOperationException or RpcException or HttpRequestException)
        {
            logger.LogWarning(ex, "Could not scan Aave events for the wallet's first activity");
            return null;
        }
    }

    private async Task<LendingActivity?> FindActivityByTransfersAsync(string owner, CancellationToken ct)
    {
        var tokens = await ReserveTokensAsync(ct);
        var transfers = await rpc.GetTokenTransfersToAsync(owner, tokens.Keys.ToList(), ct);
        if (transfers.Count == 0) return null;

        var first = transfers.Min(t => t.BlockNumber);
        var stamps = await rpc.GetBlockTimestampsAsync([first], ct);
        var reserves = transfers.Select(t => tokens[t.Contract]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LendingActivity(DateOnly.FromDateTime(stamps[first]), reserves);
    }

    /// <summary>aToken and variable debt token of every reserve, mapped to the reserve's underlying asset.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ReserveTokensAsync(CancellationToken ct)
    {
        if (_tokens is not null) return _tokens;
        var reserves = await ListReservesAsync(ct);
        var results = await rpc.BatchAsync(
            reserves.Select(r => EvmRpcClient.EthCall(_dataProvider!, Abi.Encode("getReserveTokensAddresses(address)", Abi.Word(r)))).ToList(), ct);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reserves.Count; i++)
        {
            if (results[i].Error is { } error) throw error;
            var words = Abi.Words(results[i].Result.GetString());
            if (words.Count < 3) continue;
            // Words: aToken, stable debt token (retired in v3.2), variable debt token.
            map[Abi.ToAddress(words[0])] = reserves[i];
            map[Abi.ToAddress(words[2])] = reserves[i];
        }

        return _tokens = map;
    }

    // ---- history ---------------------------------------------------------------------------------------------------

    public async Task<LendingHistory> GetHistoryAsync(
        string owner,
        IReadOnlyCollection<string> reserves,
        DateOnly from,
        DateOnly to,
        CancellationToken ct,
        IProgress<ProviderProgress>? progress = null)
    {
        await ResolveAsync(ct);
        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) dates.Add(d);
        var assets = reserves.Select(r => r.ToLowerInvariant()).Distinct().ToList();
        if (dates.Count == 0 || assets.Count == 0) return new LendingHistory([], [], null);

        var blocks = await BlocksAtEndOfDaysAsync(dates, progress, ct);
        var meta = await MetaAsync(assets, ct);

        var calls = new List<RpcCall>();
        foreach (var block in blocks)
        {
            foreach (var asset in assets)
            {
                calls.Add(EvmRpcClient.EthCallAt(_dataProvider!, UserReserveData(asset, owner), block));
                calls.Add(EvmRpcClient.EthCallAt(_pool!, Abi.Encode("getReserveNormalizedIncome(address)", Abi.Word(asset)), block));
                calls.Add(EvmRpcClient.EthCallAt(_pool!, Abi.Encode("getReserveNormalizedVariableDebt(address)", Abi.Word(asset)), block));
            }
        }

        var results = await rpc.BatchAsync(calls, ct, (done, total) => progress?.Report(
            new ProviderProgress("Reading balances at each day's close", done, total, BlockSearchShare + (1 - BlockSearchShare) * done / Math.Max(1, total))));

        var days = new List<LendingDayState>();
        var failed = new List<DateOnly>();
        string? note = null;
        var perDay = assets.Count * 3;
        for (var d = 0; d < dates.Count; d++)
        {
            var dayRows = new List<LendingDayState>();
            var ok = true;
            for (var a = 0; a < assets.Count && ok; a++)
            {
                var slice = results.Skip(d * perDay + a * 3).Take(3).ToList();
                if (slice.FirstOrDefault(r => r.Error is not null)?.Error is { } error)
                {
                    ok = false;
                    note ??= error.Message.Contains("missing trie node", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("archive", StringComparison.OrdinalIgnoreCase)
                        ? "The RPC node cannot serve historical state, so history before today is unavailable. Use an archive-capable RPC plan."
                        : $"History could not be read: {error.Message}";
                    break;
                }

                var words = Abi.Words(slice[0].Result.GetString());
                if (words.Count < 9) { ok = false; break; }
                var (symbol, decimals) = meta[assets[a]];
                dayRows.Add(new LendingDayState(
                    dates[d], assets[a], symbol, decimals, PriceKey(assets[a]),
                    TokenAmount.FromRaw(Abi.ToUInt(words[0]), decimals),
                    TokenAmount.FromRaw(Abi.ToUInt(words[1]) + Abi.ToUInt(words[2]), decimals),
                    TokenAmount.FromRaw(Abi.ToUInt(slice[1].Result.GetString()!), 27),
                    TokenAmount.FromRaw(Abi.ToUInt(slice[2].Result.GetString()!), 27),
                    Abi.ToUInt(words[8]) != 0));
            }

            if (ok) days.AddRange(dayRows);
            else failed.Add(dates[d]);
        }

        return new LendingHistory(days, failed, note);
    }

    /// <summary>
    /// The last block of each UTC day, found by searching block timestamps. Not estimated from a block rate: Base's block time
    /// changed, so a rate measured on recent blocks puts every older day on the wrong block.
    /// </summary>
    private async Task<IReadOnlyList<long>> BlocksAtEndOfDaysAsync(IReadOnlyList<DateOnly> dates, IProgress<ProviderProgress>? progress, CancellationToken ct)
    {
        var latest = await rpc.GetBlockNumberAsync(ct);
        var targets = dates.Select(d => new DateTimeOffset(d.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds() - 1).ToList();
        return await BlockLocator.LastAtOrBeforeAsync(
            targets, options.StartBlock, latest, TimestampsAsync, ct,
            (done, total) => progress?.Report(new ProviderProgress("Finding the block of each day", done, total, BlockSearchShare * done / total)));
    }

    private async Task<IReadOnlyDictionary<long, long>> TimestampsAsync(IReadOnlyCollection<long> blocks, CancellationToken ct) =>
        (await rpc.GetBlockTimestampsAsync(blocks, ct)).ToDictionary(
            kv => kv.Key, kv => new DateTimeOffset(DateTime.SpecifyKind(kv.Value, DateTimeKind.Utc)).ToUnixTimeSeconds());

    // ---- lookups ---------------------------------------------------------------------------------------------------

    private async Task ResolveAsync(CancellationToken ct)
    {
        if (_pool is not null) return;
        await _resolve.WaitAsync(ct);
        try
        {
            if (_pool is not null) return;
            if (string.IsNullOrWhiteSpace(options.PoolAddressesProvider))
                throw new InvalidOperationException("Aave v3 needs Evm:Chains:<n>:Lending:AaveV3:PoolAddressesProvider.");

            var provider = options.PoolAddressesProvider;
            var pool = Abi.ToAddress(Abi.Words(await rpc.EthCallAsync(provider, Abi.Encode("getPool()"), ct))[0]);
            _dataProvider = Abi.ToAddress(Abi.Words(await rpc.EthCallAsync(provider, Abi.Encode("getPoolDataProvider()"), ct))[0]);
            _pool = pool;
        }
        finally
        {
            _resolve.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ListReservesAsync(CancellationToken ct)
    {
        if (_reserves is not null) return _reserves;
        var words = Abi.Words(await rpc.EthCallAsync(_pool!, Abi.Encode("getReservesList()"), ct));
        var count = (int)Abi.ToUInt(words[1]);
        return _reserves = words.Skip(2).Take(count).Select(Abi.ToAddress).ToList();
    }

    private async Task<Dictionary<string, (string Symbol, int Decimals)>> MetaAsync(IEnumerable<string> reserves, CancellationToken ct)
    {
        var missing = reserves.Where(r => !_meta.ContainsKey(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
        {
            var calls = missing.SelectMany(r => new[]
            {
                EvmRpcClient.EthCall(r, Abi.Encode("symbol()")),
                EvmRpcClient.EthCall(r, Abi.Encode("decimals()"))
            }).ToList();
            var results = await rpc.BatchAsync(calls, ct);
            for (var i = 0; i < missing.Count; i++)
            {
                if (results[i * 2].Error is { } e1) throw e1;
                if (results[i * 2 + 1].Error is { } e2) throw e2;
                _meta[missing[i]] = (
                    Abi.DecodeString(results[i * 2].Result.GetString()),
                    (int)Abi.ToUInt(Abi.Words(results[i * 2 + 1].Result.GetString())[0]));
            }
        }

        return reserves.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(r => r, r => _meta[r], StringComparer.OrdinalIgnoreCase);
    }

    private static string UserReserveData(string reserve, string owner) =>
        Abi.Encode("getUserReserveData(address,address)", Abi.Word(reserve), Abi.Word(owner));
}
