using System.Numerics;
using System.Text;
using System.Text.Json;
using LpLedger.Core;
using LpLedger.Core.Models;
using LpLedger.Infrastructure.Chain;
using LpLedger.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LpLedger.Tests;

/// <summary>A refresh must find new history without reading the old blocks or receipts again.</summary>
public class ScanStoreSyncTests
{
    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string Manager = "0x4857db576ee0e45f10525ec59527bfd10beec60b";
    private const string Weth = "0x4200000000000000000000000000000000000006";
    private const string Usdc = "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";
    private const string Add = "0xaaa1", Collect = "0xaaa2";

    /// <summary>A scripted Alchemy node. Records which block each transfer query started from and how often receipts were read.</summary>
    private sealed class FakeNode : HttpMessageHandler
    {
        public long Head = 1_100;
        public readonly List<long> TransferFromBlocks = [];
        public readonly List<string> ReceiptReads = [];
        private readonly List<(string Category, string From, string To, string Hash, long Block, string Contract, int? TokenId)> _transfers = [];

        public void NftIn(string hash, long block) => _transfers.Add(("erc721", "0x9", Wallet, hash, block, Manager, 7));
        public void ExternalOut(string hash, long block) => _transfers.Add(("external", Wallet, "0x9", hash, block, "", null));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(ct).Result);
            var p = body.RootElement.GetProperty("params");
            var result = body.RootElement.GetProperty("method").GetString() switch
            {
                "eth_blockNumber" => Json($"\"0x{Head:x}\""),
                "alchemy_getAssetTransfers" => Transfers(p[0]),
                "eth_getTransactionReceipt" => Receipt(p[0].GetString()!),
                "eth_call" => Json($"\"0x{Abi.Word(0)}{Abi.Word(0)}{Abi.AddressWord(Weth)}{Abi.AddressWord(Usdc)}{Abi.Word(100)}{Abi.Word(0)}{Abi.Word(200)}{Abi.Word(1000)}\""),
                var m => throw new InvalidOperationException(m),
            };
            return Task.FromResult(new HttpResponseMessage { Content = new StringContent($"{{\"result\":{result}}}", Encoding.UTF8, "application/json") });
        }

        private static string Json(string raw) => raw;

        private string Transfers(JsonElement q)
        {
            var from = long.Parse(q.GetProperty("fromBlock").GetString()![2..], System.Globalization.NumberStyles.HexNumber);
            TransferFromBlocks.Add(from);
            var category = q.GetProperty("category")[0].GetString();
            var fromAddress = q.TryGetProperty("fromAddress", out var f) ? f.GetString() : null;
            var toAddress = q.TryGetProperty("toAddress", out var t) ? t.GetString() : null;
            var rows = _transfers
                .Where(x => x.Category == category && x.Block >= from && (fromAddress is null || x.From == fromAddress) && (toAddress is null || x.To == toAddress))
                .Select(x =>
                    $"{{\"hash\":\"{x.Hash}\",\"blockNum\":\"0x{x.Block:x}\",\"from\":\"{x.From}\",\"to\":\"{x.To}\"," +
                    $"\"rawContract\":{{\"address\":\"{x.Contract}\"}}," +
                    (x.TokenId is { } id ? $"\"tokenId\":\"0x{id:x}\"," : "") +
                    "\"metadata\":{\"blockTimestamp\":\"2024-07-01T00:00:00.000Z\"}}");
            return $"{{\"transfers\":[{string.Join(",", rows)}]}}";
        }

        private string Receipt(string hash)
        {
            ReceiptReads.Add(hash);
            var (topic, block, data) = hash == Add
                ? (Abi.Topic("IncreaseLiquidity(uint256,uint128,uint256,uint256)"), 1_000, Abi.Word(500) + Abi.Word(10) + Abi.Word(20))
                : (Abi.Topic("Collect(uint256,address,uint256,uint256)"), 4_000, Abi.AddressWord(Wallet) + Abi.Word(3) + Abi.Word(4));
            return $"{{\"blockNumber\":\"0x{block:x}\",\"logs\":[{{\"address\":\"{Manager}\"," +
                   $"\"topics\":[\"{topic}\",\"0x{Abi.Word(7)}\"],\"data\":\"0x{data}\",\"logIndex\":\"0x1\"}}]}}";
        }
    }

    private static AlchemyChainReader Reader(FakeNode node, IScanStore store) =>
        new(new AlchemyClient(new HttpClient(node), "http://node", computeUnitsPerSecond: 1e9), [Manager], store);

    private static async Task<IReadOnlyList<RawLiquidityLog>> SyncAsync(AlchemyChainReader reader)
    {
        var refs = await reader.CandidatesAsync(Wallet);
        return await reader.LiquidityLogsAsync(Wallet, refs);
    }

    private static async Task RefreshReadsOnlyNewBlocks(IScanStore store)
    {
        var node = new FakeNode();
        node.NftIn(Add, 1_000);

        var first = await SyncAsync(Reader(node, store));
        Assert.Single(first);
        Assert.Equal(EventKind.Add, first[0].Kind);
        Assert.Equal(new BigInteger(20), first[0].Amount1);
        Assert.All(node.TransferFromBlocks, b => Assert.Equal(0, b));

        // Time passes: a new transaction lands, and a fresh reader (a new request, or a restarted app) refreshes.
        node.Head = 5_000;
        node.ExternalOut(Collect, 4_000);
        node.TransferFromBlocks.Clear();
        node.ReceiptReads.Clear();

        var second = await SyncAsync(Reader(node, store));
        Assert.Equal([EventKind.Add, EventKind.Collect], second.OrderBy(l => l.BlockNumber).Select(l => l.Kind));
        Assert.Equal([Collect], node.ReceiptReads);
        Assert.All(node.TransferFromBlocks, b => Assert.Equal(1_100 - 30 + 1, b));

        // Nothing new: the transfer queries still run, but no receipt is read.
        node.ReceiptReads.Clear();
        Assert.Equal(2, (await SyncAsync(Reader(node, store))).Count);
        Assert.Empty(node.ReceiptReads);
    }

    [Fact]
    public Task Memory_store_refresh_reads_only_new_blocks() => RefreshReadsOnlyNewBlocks(new MemoryScanStore());

    [Fact]
    public async Task Database_store_refresh_reads_only_new_blocks()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerDb>().UseSqlite(conn).Options;
        await using (var db = new LedgerDb(options)) await db.Database.EnsureCreatedAsync();
        await RefreshReadsOnlyNewBlocks(new EfScanStore(new SingleConnectionFactory(options)));
    }

    [Fact]
    public async Task Database_store_keeps_history_across_store_instances()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerDb>().UseSqlite(conn).Options;
        await using (var db = new LedgerDb(options)) await db.Database.EnsureCreatedAsync();

        var node = new FakeNode();
        node.NftIn(Add, 1_000);
        await SyncAsync(Reader(node, new EfScanStore(new SingleConnectionFactory(options))));

        // A restarted app has an empty memory but the same database, and must not read the old receipt again.
        node.ReceiptReads.Clear();
        var logs = await SyncAsync(Reader(node, new EfScanStore(new SingleConnectionFactory(options))));
        Assert.Single(logs);
        Assert.Empty(node.ReceiptReads);
    }

    private sealed class SingleConnectionFactory(DbContextOptions<LedgerDb> options) : IDbContextFactory<LedgerDb>
    {
        public LedgerDb CreateDbContext() => new(options);
    }
}
