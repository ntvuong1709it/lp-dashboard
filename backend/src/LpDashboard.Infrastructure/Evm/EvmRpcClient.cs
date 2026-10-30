using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LpDashboard.Infrastructure.Evm;

public sealed class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    public bool IsRevert => Code == 3 || Message.Contains("revert", StringComparison.OrdinalIgnoreCase);

    public bool IsRateLimited => Code == 429
        || Message.Contains("compute units per second", StringComparison.OrdinalIgnoreCase)
        || Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
        || Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
}

internal sealed record RpcCall(string Method, object?[] Params);

internal sealed record RpcResult(JsonElement Result, RpcException? Error);

internal sealed record EvmLog(string Address, IReadOnlyList<string> Topics, string Data, long BlockNumber, string TxHash, int LogIndex);

internal sealed record EvmReceipt(string TxHash, string From, BigInteger FeeWei, IReadOnlyList<EvmLog> Logs);

internal sealed record NftTransfer(BigInteger TokenId, string From, string To, long BlockNumber, int LogIndex, string TxHash);

/// <summary>JSON-RPC client with batching, pacing, 429 retry and adaptive eth_getLogs ranges. Never logs the URL (it contains the API key).</summary>
internal sealed partial class EvmRpcClient(IHttpClientFactory httpClientFactory, EvmChainOptions options)
{
    public const string HttpClientName = "evm-rpc";
    private const int MaxBatchSize = 50;
    private const int MaxRetries = 6;
    private readonly SemaphoreSlim _pacing = new(1, 1);
    private readonly TimeSpan _perCall = options.MaxCallsPerSecond > 0 ? TimeSpan.FromSeconds(1 / options.MaxCallsPerSecond) : TimeSpan.Zero;
    private DateTime _nextSendAt = DateTime.MinValue;
    // Remembered across scans so the provider's range limit is only hit once.
    private long _logRange = options.MaxLogBlockRange;
    private int _nextId;

    [GeneratedRegex(@"\[0x([0-9a-fA-F]+),\s*0x([0-9a-fA-F]+)\]")]
    private static partial Regex SuggestedRange();

    public async Task<JsonElement> CallAsync(string method, object?[] parameters, CancellationToken ct)
    {
        var result = (await BatchAsync([new RpcCall(method, parameters)], ct))[0];
        if (result.Error is not null) throw result.Error;
        return result.Result;
    }

    public async Task<IReadOnlyList<RpcResult>> BatchAsync(IReadOnlyList<RpcCall> calls, CancellationToken ct, Action<int, int>? progress = null)
    {
        var results = new RpcResult[calls.Count];
        for (var offset = 0; offset < calls.Count; offset += MaxBatchSize)
        {
            var pending = Enumerable.Range(offset, Math.Min(MaxBatchSize, calls.Count - offset)).ToList();
            for (var attempt = 0; ; attempt++)
            {
                await SendBatchAsync(calls, pending, results, ct);
                // Alchemy reports CU/s throttling per item inside an HTTP 200 batch response.
                pending = pending.Where(i => results[i]?.Error?.IsRateLimited == true).ToList();
                if (pending.Count == 0 || attempt >= MaxRetries) break;
                await Task.Delay(Backoff(attempt), ct);
            }
            progress?.Invoke(Math.Min(offset + MaxBatchSize, calls.Count), calls.Count);
        }

        for (var i = 0; i < results.Length; i++)
        {
            results[i] ??= new RpcResult(default, new RpcException(-1, "Missing response in JSON-RPC batch"));
        }
        return results;
    }

    private async Task SendBatchAsync(IReadOnlyList<RpcCall> calls, IReadOnlyList<int> indices, RpcResult[] results, CancellationToken ct)
    {
        var idToIndex = new Dictionary<int, int>(indices.Count);
        var payload = new List<object>(indices.Count);
        foreach (var index in indices)
        {
            var id = Interlocked.Increment(ref _nextId);
            idToIndex[id] = index;
            var call = calls[index];
            payload.Add(new { jsonrpc = "2.0", id, method = call.Method, @params = call.Params });
        }

        using var document = await PostAsync(payload, payload.Count, ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            var batchError = ToException(root.TryGetProperty("error", out var err) ? err : root);
            if (!batchError.IsRateLimited) throw batchError;
            foreach (var index in indices) results[index] = new RpcResult(default, batchError);
            return;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement) || !idToIndex.TryGetValue(idElement.GetInt32(), out var index)) continue;
            results[index] = item.TryGetProperty("error", out var error)
                ? new RpcResult(default, ToException(error))
                : new RpcResult(item.GetProperty("result").Clone(), null);
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));

    public async Task<long> GetBlockNumberAsync(CancellationToken ct) =>
        Abi.ToLong((await CallAsync("eth_blockNumber", [], ct)).GetString()!);

    public async Task<string> EthCallAsync(string to, string data, CancellationToken ct, string? from = null) =>
        (await CallAsync("eth_call", [CallObject(to, data, from), "latest"], ct)).GetString()!;

    public static RpcCall EthCall(string to, string data, string? from = null) => new("eth_call", [CallObject(to, data, from), "latest"]);

    public async Task<List<EvmLog>> GetLogsAsync(object address, object?[] topics, long fromBlock, long toBlock, CancellationToken ct)
    {
        var logs = new List<EvmLog>();
        var range = _logRange > 0 ? _logRange : toBlock - fromBlock + 1;
        var start = fromBlock;

        while (start <= toBlock)
        {
            var requests = (toBlock - start + range) / range;
            if (requests > options.MaxLogRequestsPerScan)
            {
                throw new InvalidOperationException(
                    $"The RPC provider allows only {range} blocks per eth_getLogs request; scanning blocks {start}-{toBlock} would need ~{requests:N0} requests, " +
                    $"so the scan was stopped. Enable Evm:Chains:<n>:UseAlchemyTransfers or use an RPC plan with a larger eth_getLogs range.");
            }

            var end = Math.Min(toBlock, start + range - 1);
            try
            {
                var filter = new Dictionary<string, object?>
                {
                    ["fromBlock"] = Abi.ToHex(start),
                    ["toBlock"] = Abi.ToHex(end),
                    ["address"] = address,
                    ["topics"] = topics
                };
                var result = await CallAsync("eth_getLogs", [filter], ct);
                logs.AddRange(result.EnumerateArray().Select(ParseLog));
                start = end + 1;
            }
            catch (RpcException ex) when (end > start && !ex.IsRateLimited)
            {
                // Providers reject large ranges/results; Alchemy suggests a working range in the message.
                var match = SuggestedRange().Match(ex.Message);
                range = match.Success
                    ? Math.Max(1, Abi.ToLong(match.Groups[2].Value) - Abi.ToLong(match.Groups[1].Value) + 1)
                    : Math.Max(1, (end - start + 1) / 2);
                _logRange = range;
            }
        }

        return logs;
    }

    public async Task<Dictionary<long, DateTime>> GetBlockTimestampsAsync(IEnumerable<long> blocks, CancellationToken ct, Action<int, int>? progress = null)
    {
        var list = blocks.Distinct().ToList();
        progress?.Invoke(0, list.Count);
        var results = await BatchAsync(list.Select(b => new RpcCall("eth_getBlockByNumber", [Abi.ToHex(b), false])).ToList(), ct, progress);
        var map = new Dictionary<long, DateTime>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (results[i].Error is { } error) throw error;
            var ts = Abi.ToLong(results[i].Result.GetProperty("timestamp").GetString()!);
            map[list[i]] = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
        }
        return map;
    }

    public async Task<Dictionary<string, EvmReceipt>> GetReceiptsAsync(IEnumerable<string> txHashes, CancellationToken ct, Action<int, int>? progress = null)
    {
        var list = txHashes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        progress?.Invoke(0, list.Count);
        var results = await BatchAsync(list.Select(h => new RpcCall("eth_getTransactionReceipt", [h])).ToList(), ct, progress);
        var map = new Dictionary<string, EvmReceipt>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            if (results[i].Error is { } error) throw error;
            var r = results[i].Result;
            var gasUsed = Abi.ToUInt(r.GetProperty("gasUsed").GetString()!);
            var price = r.TryGetProperty("effectiveGasPrice", out var p) && p.ValueKind == JsonValueKind.String ? Abi.ToUInt(p.GetString()!) : BigInteger.Zero;
            // OP-stack chains (Base) add an L1 data fee on top of L2 execution gas.
            var l1Fee = r.TryGetProperty("l1Fee", out var l1) && l1.ValueKind == JsonValueKind.String ? Abi.ToUInt(l1.GetString()!) : BigInteger.Zero;
            var logs = r.GetProperty("logs").EnumerateArray().Select(ParseLog).ToList();
            map[list[i]] = new EvmReceipt(list[i], r.GetProperty("from").GetString()!.ToLowerInvariant(), gasUsed * price + l1Fee, logs);
        }
        return map;
    }

    /// <summary>All ERC-721 transfers of one contract to/from an address via alchemy_getAssetTransfers.</summary>
    public async Task<List<NftTransfer>> GetNftTransfersAsync(string contract, string? fromAddress, string? toAddress, CancellationToken ct)
    {
        var request = new Dictionary<string, object?>
        {
            ["fromBlock"] = "0x0",
            ["toBlock"] = "latest",
            ["category"] = new[] { "erc721" },
            ["contractAddresses"] = new[] { contract }
        };
        if (fromAddress is not null) request["fromAddress"] = fromAddress;
        if (toAddress is not null) request["toAddress"] = toAddress;

        return (await GetAssetTransfersAsync(request, ct)).Select(t =>
        {
            var tokenIdHex = t.TryGetProperty("erc721TokenId", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()!
                : t.GetProperty("tokenId").GetString()!;
            var uniqueId = t.TryGetProperty("uniqueId", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            var logIndex = int.TryParse(uniqueId.Split(':').LastOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var li) ? li : 0;

            return new NftTransfer(
                Abi.ToUInt(tokenIdHex),
                (t.GetProperty("from").GetString() ?? string.Empty).ToLowerInvariant(),
                (t.GetProperty("to").GetString() ?? string.Empty).ToLowerInvariant(),
                Abi.ToLong(t.GetProperty("blockNum").GetString()!),
                logIndex,
                t.GetProperty("hash").GetString()!);
        }).ToList();
    }

    /// <summary>
    /// Transactions in which the address sent ETH/tokens/NFTs or received tokens/NFTs, via alchemy_getAssetTransfers.
    /// Misses only transactions sent by others that neither move tokens to nor from the address.
    /// </summary>
    public async Task<HashSet<string>> GetTransactionHashesAsync(string address, long fromBlock, long toBlock, CancellationToken ct)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directions = new (string Key, string[] Categories)[]
        {
            ("fromAddress", new[] { "external", "erc20", "erc721", "erc1155" }),
            ("toAddress", new[] { "erc20", "erc721", "erc1155" })
        };

        foreach (var (key, categories) in directions)
        {
            var request = new Dictionary<string, object?>
            {
                ["fromBlock"] = Abi.ToHex(fromBlock),
                ["toBlock"] = Abi.ToHex(toBlock),
                ["category"] = categories,
                [key] = address
            };
            foreach (var t in await GetAssetTransfersAsync(request, ct))
            {
                hashes.Add(t.GetProperty("hash").GetString()!.ToLowerInvariant());
            }
        }

        return hashes;
    }

    private async Task<List<JsonElement>> GetAssetTransfersAsync(Dictionary<string, object?> request, CancellationToken ct)
    {
        request["withMetadata"] = false;
        request["excludeZeroValue"] = false;
        request["maxCount"] = "0x3e8";

        var transfers = new List<JsonElement>();
        string? pageKey = null;
        do
        {
            if (pageKey is not null) request["pageKey"] = pageKey;
            var result = await CallAsync("alchemy_getAssetTransfers", [request], ct);
            transfers.AddRange(result.GetProperty("transfers").EnumerateArray());
            pageKey = result.TryGetProperty("pageKey", out var pk) && pk.ValueKind == JsonValueKind.String ? pk.GetString() : null;
        } while (pageKey is not null);

        return transfers;
    }

    private static Dictionary<string, string> CallObject(string to, string data, string? from)
    {
        var call = new Dictionary<string, string> { ["to"] = to, ["data"] = data };
        if (from is not null) call["from"] = from;
        return call;
    }

    private static EvmLog ParseLog(JsonElement log) => new(
        log.GetProperty("address").GetString()!.ToLowerInvariant(),
        log.GetProperty("topics").EnumerateArray().Select(t => t.GetString()!.ToLowerInvariant()).ToList(),
        log.GetProperty("data").GetString() ?? "0x",
        Abi.ToLong(log.GetProperty("blockNumber").GetString()!),
        log.GetProperty("transactionHash").GetString()!.ToLowerInvariant(),
        (int)Abi.ToLong(log.GetProperty("logIndex").GetString()!));

    private static RpcException ToException(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
        var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "RPC error" : error.ToString();
        return new RpcException(code, message);
    }

    private async Task<JsonDocument> PostAsync(object payload, int callCount, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 0; ; attempt++)
        {
            await PaceAsync(callCount, ct);
            using var response = await client.PostAsJsonAsync(options.RpcUrl, payload, ct);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < MaxRetries)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.Zero;
                await Task.Delay(retryAfter > Backoff(attempt) ? retryAfter : Backoff(attempt), ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"RPC request failed with HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
    }

    /// <summary>Spaces requests so the average rate stays under MaxCallsPerSecond (providers bill per call, not per HTTP request).</summary>
    private async Task PaceAsync(int callCount, CancellationToken ct)
    {
        if (_perCall == TimeSpan.Zero) return;

        await _pacing.WaitAsync(ct);
        try
        {
            var wait = _nextSendAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _nextSendAt = DateTime.UtcNow + _perCall * callCount;
        }
        finally
        {
            _pacing.Release();
        }
    }
}
