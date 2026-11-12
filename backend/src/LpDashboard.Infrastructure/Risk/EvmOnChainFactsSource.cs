using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using LpDashboard.Domain.Risk;
using LpDashboard.Infrastructure.Evm;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Infrastructure.Risk;

/// <summary>
/// Reads what the node knows about a token (<c>owner()</c>, the EIP-1967 implementation and admin slots, contract code,
/// symbol) and about an Aerodrome pool's gauge. Read-only; only token and pool addresses are sent.
/// </summary>
internal sealed class EvmOnChainFactsSource(EvmRpcClientRegistry registry, ILogger<EvmOnChainFactsSource> logger) : IOnChainFactsSource
{
    private static readonly string OwnerCall = Abi.Selector("owner()");
    private static readonly string SymbolCall = Abi.Selector("symbol()");
    private static readonly string GaugeCall = Abi.Selector("gauge()");
    private static readonly string VoterCall = Abi.Selector("voter()");

    // EIP-1967: bytes32(uint256(keccak256("eip1967.proxy.implementation")) - 1) and the same for "eip1967.proxy.admin".
    private const string ImplementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
    private const string AdminSlot = "0xb53127684a568b3173ae13b9f8a6016e243e63b6e8ee1178d6a717850b5d6103";

    public bool Supports(Chain chain) => registry.For(chain) is not null;

    public async Task<SourceOutcome<OnChainReading>> GetTokenAsync(Chain chain, string token, CancellationToken ct)
    {
        if (registry.For(chain) is not { } rpc) return SourceOutcome<OnChainReading>.Fail("no RPC configured");

        try
        {
            var first = await rpc.BatchAsync(
            [
                new RpcCall("eth_getCode", [token, "latest"]),
                EvmRpcClient.EthCall(token, OwnerCall),
                EvmRpcClient.EthCall(token, SymbolCall),
                new RpcCall("eth_getStorageAt", [token, ImplementationSlot, "latest"]),
                new RpcCall("eth_getStorageAt", [token, AdminSlot, "latest"]),
            ], ct);

            // owner() and symbol() reverting is normal (no such function). The code and slot reads failing means we know nothing.
            if (Unusable(first[0]) || Unusable(first[3]) || Unusable(first[4])) return SourceOutcome<OnChainReading>.Fail("RPC error");
            var code = first[0].Result.ValueKind == System.Text.Json.JsonValueKind.String ? first[0].Result.GetString() : null;
            if (string.IsNullOrEmpty(code) || code == "0x") return SourceOutcome<OnChainReading>.Fail(OnChainReading.NoContract);

            var owner = ReadAddress(first[1]);
            var implementation = NonZero(ReadAddress(first[3]));
            var admin = NonZero(ReadAddress(first[4]));
            var symbol = ReadSymbol(first[2]);

            // Is the controller a contract (a multisig or timelock) or one key? One more round for those that exist.
            var checks = new List<string>();
            if (!RiskAddresses.IsZero(owner)) checks.Add(owner!);
            if (admin is not null) checks.Add(admin);
            var isContract = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            if (checks.Count > 0)
            {
                var codes = await rpc.BatchAsync(checks.Select(a => new RpcCall("eth_getCode", [a, "latest"])).ToList(), ct);
                for (var i = 0; i < checks.Count; i++)
                    isContract[checks[i]] = codes[i].Error is null && codes[i].Result.ValueKind == System.Text.Json.JsonValueKind.String
                        ? HasContractCode(codes[i].Result.GetString())
                        : null;
            }

            var facts = new OnChainFacts(
                owner,
                owner is not null && isContract.TryGetValue(owner, out var ownerIsContract) ? ownerIsContract : null,
                implementation,
                admin,
                admin is not null && isContract.TryGetValue(admin, out var adminIsContract) ? adminIsContract : null);
            return SourceOutcome<OnChainReading>.Ok(new OnChainReading(facts, symbol));
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("On-chain token read failed: {Type}", ex.GetType().Name);
            return SourceOutcome<OnChainReading>.Fail("RPC error");
        }
    }

    public async Task<SourceOutcome<GaugeStatus>> GetGaugeStatusAsync(Chain chain, string pool, CancellationToken ct)
    {
        if (registry.For(chain) is not { } rpc) return SourceOutcome<GaugeStatus>.Fail("no RPC configured");

        try
        {
            var gaugeResult = (await rpc.BatchAsync([EvmRpcClient.EthCall(pool, GaugeCall)], ct))[0];
            if (Unusable(gaugeResult)) return SourceOutcome<GaugeStatus>.Fail("RPC error");
            // A pool without a gauge function (Uniswap) or with the zero address has no gauge: nothing to be killed.
            var gauge = NonZero(ReadAddress(gaugeResult));
            if (gauge is null) return SourceOutcome<GaugeStatus>.Ok(new GaugeStatus(false, null));

            var voterResult = (await rpc.BatchAsync([EvmRpcClient.EthCall(gauge, VoterCall)], ct))[0];
            var voter = NonZero(ReadAddress(voterResult));
            if (voter is null) return SourceOutcome<GaugeStatus>.Fail("gauge has no voter");

            var alive = (await rpc.BatchAsync([EvmRpcClient.EthCall(voter, Abi.Encode("isAlive(address)", Abi.Word(gauge)))], ct))[0];
            if (alive.Error is not null || alive.Result.ValueKind != System.Text.Json.JsonValueKind.String || Abi.Words(alive.Result.GetString()) is not { Count: > 0 } words)
                return SourceOutcome<GaugeStatus>.Fail("could not read isAlive");
            return SourceOutcome<GaugeStatus>.Ok(new GaugeStatus(true, !Abi.ToUInt(words[0]).IsZero));
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("On-chain gauge read failed: {Type}", ex.GetType().Name);
            return SourceOutcome<GaugeStatus>.Fail("RPC error");
        }
    }

    private static bool Unusable(RpcResult r) => r.Error is not null && !r.Error.IsRevert;

    private static string? ReadAddress(RpcResult r)
    {
        if (r.Error is not null || r.Result.ValueKind != System.Text.Json.JsonValueKind.String) return null;
        var words = Abi.Words(r.Result.GetString());
        return words.Count >= 1 ? Abi.ToAddress(words[0]) : null;
    }

    private static string? ReadSymbol(RpcResult r)
    {
        if (r.Error is not null || r.Result.ValueKind != System.Text.Json.JsonValueKind.String) return null;
        try
        {
            var text = Abi.DecodeString(r.Result.GetString());
            return string.IsNullOrWhiteSpace(text) ? null : text.Length <= 32 ? text : text[..32];
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static string? NonZero(string? address) => RiskAddresses.IsZero(address) ? null : address;

    /// <summary>
    /// Contract code, but not the 23-byte <c>0xef0100…</c> delegation marker an EIP-7702 account carries: that is still one key.
    /// </summary>
    internal static bool HasContractCode(string? code) =>
        !string.IsNullOrEmpty(code) && code != "0x" && !(code.StartsWith("0xef0100", StringComparison.OrdinalIgnoreCase) && code.Length == 2 + 46);
}
