using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace LpLedger.Infrastructure.Chain;

public sealed class ChainRpcException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;

    /// <summary>True when the node reports a contract revert (eth_call on a missing token, for example).</summary>
    public bool IsRevert => Code == 3 || Message.Contains("revert", StringComparison.OrdinalIgnoreCase);
}

public sealed record AssetTransfer(
    string Hash, long Block, long Timestamp, string From, string To, string Contract, BigInteger? TokenId);

/// <summary>
/// JSON-RPC over HTTP for an Alchemy endpoint, including its asset-transfer index. Requests are paced to a compute-unit
/// budget per second, because the free tier answers bursts with HTTP 429.
/// </summary>
public sealed class AlchemyClient(HttpClient http, string url, int maxConcurrent = 4, double computeUnitsPerSecond = 300)
{
    private readonly SemaphoreSlim _gate = new(maxConcurrent);
    private readonly object _paceLock = new();
    private long _nextStart;

    private static int Cost(string method) => method switch
    {
        "alchemy_getAssetTransfers" => 150,
        "eth_call" => 26,
        _ => 20,
    };

    private async Task PaceAsync(string method)
    {
        long wait;
        lock (_paceLock)
        {
            var now = Stopwatch.GetTimestamp();
            var start = Math.Max(now, _nextStart);
            _nextStart = start + (long)(Cost(method) / computeUnitsPerSecond * Stopwatch.Frequency);
            wait = start - now;
        }
        if (wait > 0) await Task.Delay(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency));
    }

    public async Task<JsonElement> CallAsync(string method, params object?[] args)
    {
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = args });
        await _gate.WaitAsync();
        try
        {
            await PaceAsync(method);
            using var res = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await res.Content.ReadAsStringAsync();
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch (JsonException) { throw new HttpRequestException($"RPC returned HTTP {(int)res.StatusCode}."); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                    throw new ChainRpcException(
                        err.GetProperty("message").GetString() ?? "RPC error",
                        err.TryGetProperty("code", out var c) ? c.GetInt32() : 0);
                return root.GetProperty("result").Clone();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> EthCallAsync(string to, string data, string? from = null, long? block = null)
    {
        var call = new Dictionary<string, string> { ["to"] = to, ["data"] = data };
        if (from is not null) call["from"] = from;
        var result = await CallAsync("eth_call", call, block is null ? "latest" : "0x" + block.Value.ToString("x"));
        return result.GetString() ?? "0x";
    }

    public async Task<long> BlockNumberAsync() =>
        long.Parse((await CallAsync("eth_blockNumber")).GetString()![2..], NumberStyles.HexNumber);

    /// <summary>Transfers from Alchemy's index, from fromBlock to the head and without a block-range limit.</summary>
    public async Task<List<AssetTransfer>> TransfersAsync(
        string category, string? from, string? to, IEnumerable<string>? contracts = null, bool includeZeroValue = false,
        long fromBlock = 0)
    {
        var all = new List<AssetTransfer>();
        string? pageKey = null;
        do
        {
            var q = new Dictionary<string, object>
            {
                ["fromBlock"] = "0x" + fromBlock.ToString("x"), ["toBlock"] = "latest", ["category"] = new[] { category },
                ["maxCount"] = "0x3e8", ["withMetadata"] = true, ["excludeZeroValue"] = !includeZeroValue,
            };
            if (from is not null) q["fromAddress"] = from;
            if (to is not null) q["toAddress"] = to;
            if (contracts is not null) q["contractAddresses"] = contracts.ToArray();
            if (pageKey is not null) q["pageKey"] = pageKey;

            var result = await CallAsync("alchemy_getAssetTransfers", q);
            foreach (var t in result.GetProperty("transfers").EnumerateArray())
                all.Add(Parse(t));
            pageKey = result.TryGetProperty("pageKey", out var pk) && pk.ValueKind == JsonValueKind.String ? pk.GetString() : null;
        } while (pageKey is not null);
        return all;
    }

    private static AssetTransfer Parse(JsonElement t)
    {
        static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.ToLowerInvariant() : "";
        var block = long.Parse(Str(t, "blockNum")[2..], NumberStyles.HexNumber);
        var ts = t.TryGetProperty("metadata", out var m) && m.TryGetProperty("blockTimestamp", out var bt)
            ? DateTimeOffset.Parse(bt.GetString()!, CultureInfo.InvariantCulture).ToUnixTimeSeconds() : 0;
        BigInteger? id = t.TryGetProperty("tokenId", out var tid) && tid.ValueKind == JsonValueKind.String
            ? Abi.ToUInt(tid.GetString()![2..].PadLeft(64, '0')) : null;
        var contract = t.TryGetProperty("rawContract", out var rc) ? Str(rc, "address") : "";
        return new AssetTransfer(Str(t, "hash"), block, ts, Str(t, "from"), Str(t, "to"), contract, id);
    }
}
