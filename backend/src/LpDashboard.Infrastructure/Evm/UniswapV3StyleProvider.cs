using System.Collections.Concurrent;
using System.Numerics;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Infrastructure.Evm;

/// <summary>
/// Uniswap v3 NonfungiblePositionManager and its forks (Aerodrome Slipstream).
/// Slipstream differs by: tickSpacing instead of fee in positions()/getPool(), and NFTs staked in CL gauges earn AERO instead of swap fees.
/// </summary>
internal sealed class UniswapV3StyleProvider : IPositionProvider
{
    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";
    private const int TokenIdsPerLogQuery = 100;

    private static readonly string IncreaseTopic = Abi.Topic("IncreaseLiquidity(uint256,uint128,uint256,uint256)");
    // Slipstream V3 position managers append the position's ticks to IncreaseLiquidity.
    private static readonly string IncreaseWithTicksTopic = Abi.Topic("IncreaseLiquidity(uint256,uint128,uint256,uint256,int24,int24)");
    private static readonly string DecreaseTopic = Abi.Topic("DecreaseLiquidity(uint256,uint128,uint256,uint256)");
    private static readonly string CollectTopic = Abi.Topic("Collect(uint256,address,uint256,uint256)");
    private static readonly string TransferTopic = Abi.Topic("Transfer(address,address,uint256)");
    private static readonly string PoolMintTopic = Abi.Topic("Mint(address,address,int24,int24,uint128,uint256,uint256)");
    private static readonly string GaugeClaimRewardsTopic = Abi.Topic("ClaimRewards(address,uint256)");
    // Slipstream V3 gauges report emissions per tokenId. Deferred emissions are owed to the account and later paid without a tokenId,
    // so they count as claimed when deferred and the later DeferredEmissionsClaimed is ignored.
    private static readonly string GaugeEmissionsClaimedTopic = Abi.Topic("EmissionsClaimed(address,uint256,address,uint256)");
    private static readonly string GaugeEmissionsDeferredTopic = Abi.Topic("EmissionsDeferred(address,uint256,uint256)");

    private static readonly string[] IncreaseTopics = [IncreaseTopic, IncreaseWithTicksTopic];
    private static readonly string[] GaugeRewardTopics = [GaugeClaimRewardsTopic, GaugeEmissionsClaimedTopic, GaugeEmissionsDeferredTopic];

    private readonly EvmRpcClient _rpc;
    private readonly EvmChainOptions _chain;
    private readonly EvmProtocolOptions _protocol;
    private readonly ILogger _logger;
    private readonly bool _isSlipstream;
    private readonly Dictionary<string, EvmDeploymentOptions> _deployments;

    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _poolByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int? Fee, int TickSpacing)> _poolParams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TokenInfo> _gaugeRewardTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _gaugeEarnedTakesTokenArray = new(StringComparer.OrdinalIgnoreCase);

    public UniswapV3StyleProvider(EvmRpcClient rpc, EvmChainOptions chain, EvmProtocolOptions protocol, ILogger logger)
    {
        _rpc = rpc;
        _chain = chain;
        _protocol = protocol;
        _logger = logger;
        _isSlipstream = protocol.Protocol == Protocol.AerodromeSlipstream;
        _deployments = protocol.Deployments.ToDictionary(d => d.PositionManager.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    }

    public static bool Supports(Protocol protocol) => protocol is Protocol.UniswapV3 or Protocol.AerodromeSlipstream;

    public Chain Chain => _chain.Chain;
    public Protocol Protocol => _protocol.Protocol;
    public string NativePriceKey => _chain.NativePriceKey;

    public string GetPriceKey(string tokenAddress) => $"{_chain.PriceChainKey}:{tokenAddress.ToLowerInvariant()}";

    public Task<long> GetLatestBlockAsync(CancellationToken ct) => _rpc.GetBlockNumberAsync(ct);

    public async Task<IReadOnlyList<DiscoveredPosition>> DiscoverAsync(string owner, CancellationToken ct)
    {
        owner = owner.ToLowerInvariant();
        var result = new List<DiscoveredPosition>();
        foreach (var npm in _deployments.Keys)
        {
            result.AddRange(await DiscoverAsync(npm, owner, ct));
        }
        return result;
    }

    private async Task<List<DiscoveredPosition>> DiscoverAsync(string npm, string owner, CancellationToken ct)
    {
        var transfers = await GetTransfersAsync(npm, owner, ct);

        var gauges = await FilterGaugesAsync(npm, OutgoingTargets(transfers, owner), ct);
        transfers = await ResolveIntermediariesAsync(npm, owner, transfers, gauges, ct);
        gauges.UnionWith(await FilterGaugesAsync(npm, OutgoingTargets(transfers, owner).Except(gauges).ToList(), ct));

        var result = new List<DiscoveredPosition>();
        foreach (var group in transfers.GroupBy(t => t.TokenId))
        {
            var ordered = group.OrderBy(t => t.BlockNumber).ThenBy(t => t.LogIndex).ToList();
            var firstIn = ordered.FirstOrDefault(t => t.To == owner);
            if (firstIn is null) continue;

            var last = ordered[^1];
            var gauge = ordered.LastOrDefault(t => gauges.Contains(t.To) || gauges.Contains(t.From)) is { } g
                ? (gauges.Contains(g.To) ? g.To : g.From)
                : null;

            var isHeld = last.To == owner;
            var isStaked = last.From == owner && gauges.Contains(last.To);
            var isBurned = last.From == owner && last.To == ZeroAddress;

            // Transferred away to someone else: no longer this owner's position.
            if (!isHeld && !isStaked && !isBurned) continue;

            result.Add(new DiscoveredPosition(npm, group.Key.ToString(), firstIn.BlockNumber, isStaked, gauge, isBurned,
                _isSlipstream ? GetStakePeriods(ordered, owner, gauges) : null));
        }

        return result;
    }

    private static List<string> OutgoingTargets(IEnumerable<NftTransfer> transfers, string owner) =>
        transfers.Where(t => t.From == owner && t.To != ZeroAddress && t.To != owner).Select(t => t.To).Distinct().ToList();

    /// <summary>
    /// Routers, zaps and migrators pass the NFT through themselves within one tx (owner → router → gauge, gauge → router → owner,
    /// owner → migrator → burn). Replace each such transfer's counterparty with the effective one so staking and burning are recognised.
    /// </summary>
    private async Task<List<NftTransfer>> ResolveIntermediariesAsync(
        string npm, string owner, List<NftTransfer> transfers, HashSet<string> gauges, CancellationToken ct)
    {
        bool IsEndpoint(string address) => address == ZeroAddress || address == owner || gauges.Contains(address);
        var indirect = transfers.Where(t => !IsEndpoint(t.From == owner ? t.To : t.From)).ToHashSet();
        if (indirect.Count == 0) return transfers;

        var receipts = await _rpc.GetReceiptsAsync(indirect.Select(t => t.TxHash), ct);
        var result = new List<NftTransfer>(transfers.Count);
        foreach (var t in transfers)
        {
            if (!indirect.Contains(t))
            {
                result.Add(t);
                continue;
            }

            var tokenTopic = Abi.TopicOf(t.TokenId);
            var hops = receipts[t.TxHash].Logs
                .Where(l => l.Address == npm && l.Topics.Count == 4 && l.Topics[0] == TransferTopic && l.Topics[3] == tokenTopic)
                .OrderBy(l => l.LogIndex)
                .Select(l => (From: Abi.ToAddress(l.Topics[1]), To: Abi.ToAddress(l.Topics[2])))
                .ToList();
            var index = hops.IndexOf((t.From, t.To));
            if (index < 0)
            {
                result.Add(t);
                continue;
            }

            if (t.From == owner)
            {
                var to = t.To;
                for (var i = index + 1; i < hops.Count && to != owner; i++)
                {
                    if (hops[i].From == to) to = hops[i].To;
                }
                // Round trip back to the owner within the tx: nothing changed hands.
                if (to != owner) result.Add(t with { To = to });
            }
            else
            {
                var from = t.From;
                for (var i = index - 1; i >= 0 && from != owner; i--)
                {
                    if (hops[i].To == from) from = hops[i].From;
                }
                if (from != owner) result.Add(t with { From = from });
            }
        }
        return result;
    }

    private static List<StakePeriod> GetStakePeriods(List<NftTransfer> ordered, string owner, HashSet<string> gauges)
    {
        var periods = new List<StakePeriod>();
        foreach (var t in ordered)
        {
            if (t.From == owner && gauges.Contains(t.To))
            {
                periods.Add(new StakePeriod(t.To, t.BlockNumber, t.LogIndex));
            }
            else if (gauges.Contains(t.From) && periods.Count > 0 && periods[^1].ToBlock is null
                && periods[^1].Gauge.Equals(t.From, StringComparison.OrdinalIgnoreCase))
            {
                periods[^1] = periods[^1] with { ToBlock = t.BlockNumber, ToLogIndex = t.LogIndex };
            }
        }
        return periods;
    }

    public async Task<IReadOnlyList<RawPositionEvent>> GetEventsAsync(
        string owner, IReadOnlyList<DiscoveredPosition> positions, long fromBlock, long toBlock, CancellationToken ct, IProgress<ProviderProgress>? progress = null)
    {
        owner = owner.ToLowerInvariant();
        var gaugeByPosition = positions.Where(p => p.Gauge is not null).ToDictionary(p => (p.PositionManager, p.TokenId), p => p.Gauge!);
        var membersByGauge = positions.Where(p => p.Gauge is not null).GroupBy(p => p.Gauge!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var logs = new List<(EvmLog Log, string Npm, string TokenId, PositionEventType Type, BigInteger A0, BigInteger A1, BigInteger Reward, TokenInfo? RewardToken)>();

        List<EvmLog> candidates;
        Dictionary<string, EvmReceipt>? ownerReceipts = null;
        if (_chain.UseAlchemyTransfers)
        {
            (candidates, ownerReceipts) = await GetLogsFromOwnerTransactionsAsync(owner, positions, membersByGauge.Keys, fromBlock, toBlock, progress, ct);
        }
        else
        {
            candidates = await GetLogsByFilterAsync(owner, positions, membersByGauge.Keys, fromBlock, toBlock, progress, ct);
        }

        foreach (var log in candidates)
        {
            if (log.Topics[0] == GaugeClaimRewardsTopic)
            {
                // ClaimRewards has no tokenId; split evenly across this owner's positions staked in the gauge at that moment.
                var members = membersByGauge[log.Address]
                    .Where(m => m.StakePeriods is null || m.WasStakedIn(log.Address, log.BlockNumber, log.LogIndex))
                    .ToList();
                if (members.Count == 0)
                {
                    _logger.LogWarning("Gauge claim {TxHash}:{LogIndex} matches no staked position; skipped", log.TxHash, log.LogIndex);
                    continue;
                }

                var rewardToken = await GetGaugeRewardTokenAsync(members[0].PositionManager, log.Address, ct);
                var amount = Abi.ToUInt(Abi.Words(log.Data)[0]);
                var share = amount / members.Count;
                foreach (var member in members)
                {
                    logs.Add((log, member.PositionManager, member.TokenId, PositionEventType.RewardClaim, 0, 0, share, rewardToken));
                }
                continue;
            }

            if (log.Topics[0] == GaugeEmissionsClaimedTopic || log.Topics[0] == GaugeEmissionsDeferredTopic)
            {
                var stakedTokenId = Abi.ToUInt(log.Topics[2]).ToString();
                var member = membersByGauge.GetValueOrDefault(log.Address)?.FirstOrDefault(m => m.TokenId == stakedTokenId);
                if (member is null)
                {
                    _logger.LogWarning("Gauge emissions {TxHash}:{LogIndex} for token {TokenId} match no tracked position; skipped", log.TxHash, log.LogIndex, stakedTokenId);
                    continue;
                }

                var rewardToken = await GetGaugeRewardTokenAsync(member.PositionManager, log.Address, ct);
                logs.Add((log, member.PositionManager, member.TokenId, PositionEventType.RewardClaim, 0, 0, Abi.ToUInt(Abi.Words(log.Data)[0]), rewardToken));
                continue;
            }

            var npm = log.Address;
            var tokenId = Abi.ToUInt(log.Topics[1]).ToString();
            var words = Abi.Words(log.Data);
            if (IncreaseTopics.Contains(log.Topics[0]))
            {
                logs.Add((log, npm, tokenId, PositionEventType.IncreaseLiquidity, Abi.ToUInt(words[1]), Abi.ToUInt(words[2]), 0, null));
            }
            else if (log.Topics[0] == DecreaseTopic)
            {
                logs.Add((log, npm, tokenId, PositionEventType.DecreaseLiquidity, Abi.ToUInt(words[1]), Abi.ToUInt(words[2]), 0, null));
            }
            else if (log.Topics[0] == CollectTopic)
            {
                var recipient = Abi.ToAddress(words[0]);
                // Gauge-initiated collects of staked liquidity go to voters, not the LP.
                if (gaugeByPosition.TryGetValue((npm, tokenId), out var gauge) && recipient == gauge) continue;
                logs.Add((log, npm, tokenId, PositionEventType.Collect, Abi.ToUInt(words[1]), Abi.ToUInt(words[2]), 0, null));
            }
        }

        if (logs.Count == 0) return [];

        var timestamps = await _rpc.GetBlockTimestampsAsync(logs.Select(l => l.Log.BlockNumber), ct,
            (done, total) => progress?.Report(new ProviderProgress("Fetching block timestamps", done, total)));
        var receipts = ownerReceipts ?? await _rpc.GetReceiptsAsync(logs.Select(l => l.Log.TxHash), ct,
            (done, total) => progress?.Report(new ProviderProgress("Fetching transaction receipts", done, total)));

        return logs.Select(l =>
        {
            var receipt = receipts[l.Log.TxHash];
            return new RawPositionEvent(
                l.Npm,
                l.TokenId,
                l.Type,
                l.Log.TxHash,
                l.Log.LogIndex,
                l.Log.BlockNumber,
                timestamps[l.Log.BlockNumber],
                l.A0,
                l.A1,
                l.RewardToken,
                l.Reward,
                receipt.From,
                TokenAmount.FromRaw(receipt.FeeWei, 18));
        }).ToList();
    }

    private async Task<List<EvmLog>> GetLogsByFilterAsync(
        string owner, IReadOnlyList<DiscoveredPosition> positions, IEnumerable<string> gauges, long fromBlock, long toBlock,
        IProgress<ProviderProgress>? progress, CancellationToken ct)
    {
        var gaugeList = gauges.ToList();
        var chunks = positions.GroupBy(p => p.PositionManager)
            .SelectMany(byNpm => byNpm.Select(p => p.TokenId).Chunk(TokenIdsPerLogQuery).Select(chunk => (Npm: byNpm.Key, TokenIds: chunk)))
            .ToList();
        var total = chunks.Count + gaugeList.Count;
        var done = 0;
        void Report() => progress?.Report(new ProviderProgress("Scanning event logs", done, total));

        var logs = new List<EvmLog>();
        Report();
        foreach (var (npm, tokenIds) in chunks)
        {
            var tokenTopics = tokenIds.Select(id => Abi.TopicOf(BigInteger.Parse(id))).ToArray();
            logs.AddRange(await _rpc.GetLogsAsync(npm, [new[] { IncreaseTopic, IncreaseWithTicksTopic, DecreaseTopic, CollectTopic }, tokenTopics], fromBlock, toBlock, ct));
            done++;
            Report();
        }

        foreach (var gauge in gaugeList)
        {
            logs.AddRange(await _rpc.GetLogsAsync(gauge, [GaugeRewardTopics, Abi.TopicOf(owner)], fromBlock, toBlock, ct));
            done++;
            Report();
        }
        return logs;
    }

    /// <summary>Avoids eth_getLogs (Alchemy Free tier allows 10 blocks per request) by reading receipts of the owner's transactions.</summary>
    private async Task<(List<EvmLog> Logs, Dictionary<string, EvmReceipt> Receipts)> GetLogsFromOwnerTransactionsAsync(
        string owner, IReadOnlyList<DiscoveredPosition> positions, IEnumerable<string> gauges, long fromBlock, long toBlock,
        IProgress<ProviderProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ProviderProgress("Listing wallet transactions", 0, 0));
        var hashes = await _rpc.GetTransactionHashesAsync(owner, fromBlock, toBlock, ct);
        var receipts = await _rpc.GetReceiptsAsync(hashes, ct,
            (done, total) => progress?.Report(new ProviderProgress("Fetching transaction receipts", done, total)));

        var npmEvents = new HashSet<string> { IncreaseTopic, IncreaseWithTicksTopic, DecreaseTopic, CollectTopic };
        var tokens = positions.Select(p => (p.PositionManager, Abi.TopicOf(BigInteger.Parse(p.TokenId)))).ToHashSet();
        var gaugeSet = gauges.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownerTopic = Abi.TopicOf(owner);

        var logs = receipts.Values
            .SelectMany(r => r.Logs)
            .Where(l => l.Topics.Count > 1 && l.BlockNumber >= fromBlock && l.BlockNumber <= toBlock)
            .Where(l => (npmEvents.Contains(l.Topics[0]) && tokens.Contains((l.Address, l.Topics[1])))
                || (GaugeRewardTopics.Contains(l.Topics[0]) && l.Topics[1] == ownerTopic && gaugeSet.Contains(l.Address)))
            .ToList();
        return (logs, receipts);
    }

    public async Task<PositionChainState> GetStateAsync(
        string owner, DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, CancellationToken ct)
    {
        owner = owner.ToLowerInvariant();
        var npm = position.PositionManager;
        var tokenId = BigInteger.Parse(position.TokenId);
        var block = await _rpc.GetBlockNumberAsync(ct);

        string positionData;
        try
        {
            positionData = await _rpc.EthCallAsync(npm, Abi.Encode("positions(uint256)", Abi.Word(tokenId)), ct);
        }
        catch (RpcException ex) when (ex.IsRevert)
        {
            return await GetBurnedStateAsync(position, knownTxHashes, block, ct);
        }

        var w = Abi.Words(positionData);
        var token0 = Abi.ToAddress(w[2]);
        var token1 = Abi.ToAddress(w[3]);
        var feeOrSpacing = (int)Abi.ToInt(w[4]);
        var tickLower = (int)Abi.ToInt(w[5]);
        var tickUpper = (int)Abi.ToInt(w[6]);
        var liquidity = Abi.ToUInt(w[7]);

        var pool = await GetPoolAddressAsync(_deployments[npm].Factory, token0, token1, feeOrSpacing, ct);
        var (sqrtPriceX96, currentTick, fee, tickSpacing) = await GetPoolStateAsync(pool, ct);
        var t0 = await GetTokenAsync(token0, ct);
        var t1 = await GetTokenAsync(token1, ct);

        BigInteger collectable0 = 0, collectable1 = 0, pending = 0;
        TokenInfo? rewardToken = null;

        if (position.IsStaked && position.Gauge is not null)
        {
            rewardToken = await GetGaugeRewardTokenAsync(npm, position.Gauge, ct);
            pending = await GetEarnedAsync(position.Gauge, owner, tokenId, ct);
        }
        else
        {
            // Static collect from the owner returns fees + principal owed without sending a tx.
            var collect = await _rpc.EthCallAsync(
                npm,
                Abi.Encode("collect((uint256,address,uint128,uint128))", Abi.Word(tokenId), Abi.Word(owner), Abi.Word(Abi.MaxUint128), Abi.Word(Abi.MaxUint128)),
                ct,
                from: owner);
            var cw = Abi.Words(collect);
            collectable0 = Abi.ToUInt(cw[0]);
            collectable1 = Abi.ToUInt(cw[1]);
        }

        return new PositionChainState(
            block, pool, t0, t1, fee, tickSpacing, tickLower, tickUpper, liquidity, sqrtPriceX96, currentTick,
            collectable0, collectable1, rewardToken, pending, IsBurned: false);
    }

    private async Task<PositionChainState> GetBurnedStateAsync(
        DiscoveredPosition position, IReadOnlyCollection<string> knownTxHashes, long block, CancellationToken ct)
    {
        var npm = position.PositionManager;
        var tokenTopic = Abi.TopicOf(BigInteger.Parse(position.TokenId));
        var npmTopic = Abi.TopicOf(npm);
        var receipts = await _rpc.GetReceiptsAsync(knownTxHashes, ct);

        foreach (var receipt in receipts.Values)
        {
            var increase = receipt.Logs.FirstOrDefault(l => l.Address == npm && l.Topics.Count > 1 && IncreaseTopics.Contains(l.Topics[0]) && l.Topics[1] == tokenTopic);
            if (increase is null) continue;

            var mint = receipt.Logs
                .Where(l => l.LogIndex < increase.LogIndex && l.Topics.Count == 4 && l.Topics[0] == PoolMintTopic && l.Topics[1] == npmTopic)
                .MaxBy(l => l.LogIndex);
            if (mint is null) continue;

            var pool = mint.Address;
            var tokenCalls = await _rpc.BatchAsync(
            [
                EvmRpcClient.EthCall(pool, Abi.Selector("token0()")),
                EvmRpcClient.EthCall(pool, Abi.Selector("token1()"))
            ], ct);
            var t0 = await GetTokenAsync(Abi.ToAddress(Abi.Words(tokenCalls[0].Result.GetString())[0]), ct);
            var t1 = await GetTokenAsync(Abi.ToAddress(Abi.Words(tokenCalls[1].Result.GetString())[0]), ct);
            var (sqrtPriceX96, currentTick, fee, tickSpacing) = await GetPoolStateAsync(pool, ct);

            return new PositionChainState(
                block, pool, t0, t1, fee, tickSpacing,
                (int)Abi.ToInt(mint.Topics[2]), (int)Abi.ToInt(mint.Topics[3]),
                BigInteger.Zero, sqrtPriceX96, currentTick, 0, 0, null, 0, IsBurned: true);
        }

        throw new InvalidOperationException($"Position {position.TokenId} is burned and its pool could not be resolved from known transactions.");
    }

    private async Task<List<NftTransfer>> GetTransfersAsync(string npm, string owner, CancellationToken ct)
    {
        if (_chain.UseAlchemyTransfers)
        {
            var incoming = await _rpc.GetNftTransfersAsync(npm, null, owner, ct);
            var outgoing = await _rpc.GetNftTransfersAsync(npm, owner, null, ct);
            return incoming.Concat(outgoing).ToList();
        }

        var latest = await _rpc.GetBlockNumberAsync(ct);
        var startBlock = _deployments[npm].StartBlock;
        var ownerTopic = Abi.TopicOf(owner);
        var logs = new List<EvmLog>();
        logs.AddRange(await _rpc.GetLogsAsync(npm, [TransferTopic, null, ownerTopic], startBlock, latest, ct));
        logs.AddRange(await _rpc.GetLogsAsync(npm, [TransferTopic, ownerTopic], startBlock, latest, ct));
        return logs
            .Where(l => l.Topics.Count == 4)
            .Select(l => new NftTransfer(Abi.ToUInt(l.Topics[3]), Abi.ToAddress(l.Topics[1]), Abi.ToAddress(l.Topics[2]), l.BlockNumber, l.LogIndex, l.TxHash))
            .ToList();
    }

    /// <summary>A Slipstream CL gauge exposes nft() returning the position manager.</summary>
    private async Task<HashSet<string>> FilterGaugesAsync(string npm, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        var gauges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_isSlipstream || candidates.Count == 0) return gauges;

        var results = await _rpc.BatchAsync(candidates.Select(c => EvmRpcClient.EthCall(c, Abi.Selector("nft()"))).ToList(), ct);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (results[i].Error is not null) continue;
            var words = Abi.Words(results[i].Result.GetString());
            if (words.Count > 0 && Abi.ToAddress(words[0]) == npm) gauges.Add(candidates[i]);
        }
        return gauges;
    }

    private async Task<string> GetPoolAddressAsync(string factory, string token0, string token1, int feeOrSpacing, CancellationToken ct)
    {
        var key = $"{factory}:{token0}:{token1}:{feeOrSpacing}";
        if (_poolByKey.TryGetValue(key, out var cached)) return cached;

        var signature = _isSlipstream ? "getPool(address,address,int24)" : "getPool(address,address,uint24)";
        var result = await _rpc.EthCallAsync(factory, Abi.Encode(signature, Abi.Word(token0), Abi.Word(token1), Abi.Word(feeOrSpacing)), ct);
        var pool = Abi.ToAddress(Abi.Words(result)[0]);
        if (pool == ZeroAddress)
        {
            throw new InvalidOperationException($"Factory {factory} returned no pool for {token0}/{token1}/{feeOrSpacing}. Check the configured factory address.");
        }
        return _poolByKey[key] = pool;
    }

    private async Task<(BigInteger SqrtPriceX96, int Tick, int? Fee, int TickSpacing)> GetPoolStateAsync(string pool, CancellationToken ct)
    {
        var calls = new List<RpcCall> { EvmRpcClient.EthCall(pool, Abi.Selector("slot0()")) };
        var needParams = !_poolParams.ContainsKey(pool);
        if (needParams)
        {
            calls.Add(EvmRpcClient.EthCall(pool, Abi.Selector("fee()")));
            calls.Add(EvmRpcClient.EthCall(pool, Abi.Selector("tickSpacing()")));
        }

        var results = await _rpc.BatchAsync(calls, ct);
        if (results[0].Error is { } error) throw error;
        var slot0 = Abi.Words(results[0].Result.GetString());

        if (needParams)
        {
            int? fee = results[1].Error is null ? (int)Abi.ToUInt(Abi.Words(results[1].Result.GetString())[0]) : null;
            var spacing = results[2].Error is null ? (int)Abi.ToInt(Abi.Words(results[2].Result.GetString())[0]) : 0;
            _poolParams[pool] = (fee, spacing);
        }

        var (poolFee, tickSpacing) = _poolParams[pool];
        return (Abi.ToUInt(slot0[0]), (int)Abi.ToInt(slot0[1]), poolFee, tickSpacing);
    }

    private async Task<TokenInfo> GetTokenAsync(string address, CancellationToken ct)
    {
        if (_tokens.TryGetValue(address, out var cached)) return cached;

        var results = await _rpc.BatchAsync(
        [
            EvmRpcClient.EthCall(address, Abi.Selector("symbol()")),
            EvmRpcClient.EthCall(address, Abi.Selector("decimals()"))
        ], ct);

        var symbol = results[0].Error is null ? Abi.DecodeString(results[0].Result.GetString()) : "???";
        if (results[1].Error is { } error) throw error;
        var decimals = (int)Abi.ToUInt(Abi.Words(results[1].Result.GetString())[0]);

        if (results[0].Error is not null)
        {
            _logger.LogWarning("Could not read symbol() of token {Token}", address);
        }

        return _tokens[address] = new TokenInfo(address.ToLowerInvariant(), symbol, decimals);
    }

    private async Task<TokenInfo> GetGaugeRewardTokenAsync(string npm, string gauge, CancellationToken ct)
    {
        if (_gaugeRewardTokens.TryGetValue(gauge, out var cached)) return cached;

        // Slipstream V3 gauges have no rewardToken(); their emission token is configured per deployment.
        var configured = _deployments[npm].GaugeRewardToken;
        var address = string.IsNullOrWhiteSpace(configured)
            ? Abi.ToAddress(Abi.Words(await _rpc.EthCallAsync(gauge, Abi.Selector("rewardToken()"), ct))[0])
            : configured.ToLowerInvariant();
        var token = await GetTokenAsync(address, ct);
        return _gaugeRewardTokens[gauge] = token;
    }

    private async Task<BigInteger> GetEarnedAsync(string gauge, string owner, BigInteger tokenId, CancellationToken ct)
    {
        if (!_gaugeEarnedTakesTokenArray.GetValueOrDefault(gauge))
        {
            try
            {
                var earned = await _rpc.EthCallAsync(gauge, Abi.Encode("earned(address,uint256)", Abi.Word(owner), Abi.Word(tokenId)), ct);
                return Abi.ToUInt(Abi.Words(earned)[0]);
            }
            catch (RpcException ex) when (ex.IsRevert)
            {
                // Slipstream V3 gauges only offer earned(address,uint256[]).
                _gaugeEarnedTakesTokenArray[gauge] = true;
            }
        }

        var result = await _rpc.EthCallAsync(gauge,
            Abi.Encode("earned(address,uint256[])", Abi.Word(owner), Abi.Word(64), Abi.Word(1), Abi.Word(tokenId)), ct);
        return Abi.ToUInt(Abi.Words(result)[0]);
    }
}
