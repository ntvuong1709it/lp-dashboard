using LpDashboard.Infrastructure.Evm;
using Xunit;

namespace LpDashboard.Domain.Tests;

/// <summary>A zap mints the NFT and stakes it for the owner in one tx, so the owner never appears in a transfer.</summary>
public class MediatedStakeTests
{
    private const string Npm = "0x4857db576ee0e45f10525ec59527bfd10beec60b";
    private const string Owner = "0x564be4c6031d055ab6cb638c0e65c95f4e3257ee";
    private const string Zap = "0x0fef9e88a7b76bcdbf451780c6774fe32965ee98";
    private const string Gauge = "0x46a9dca72d78366571be583aa69c1dc16e6889fb";
    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";
    private static readonly string Transfer = Abi.Topic("Transfer(address,address,uint256)");
    private const string Deposit = "0xc9817c7b5bf7b2417ea338292b8681857834d37427fbb1899f98e6897f3fe426";

    private static EvmLog Log(string address, int index, params string[] topics) =>
        new(address, topics, "0x", 55_000_000, "0xabc", index);

    private static string T(string address) => Abi.TopicOf(address);
    private static string T(int id) => Abi.TopicOf(new System.Numerics.BigInteger(id));

    [Fact]
    public void MintedToZapAndStakedForOwner_IsFound()
    {
        var logs = new[]
        {
            Log(Npm, 49, Transfer, T(ZeroAddress), T(Zap), T(2025413)),
            Log(Npm, 59, Transfer, T(Zap), T(Gauge), T(2025413)),
            Log(Gauge, 60, Deposit, T(Zap), T(Owner), T(2025413))
        };

        var stake = Assert.Single(UniswapV3StyleProvider.FindMediatedStakes(logs, Npm, Owner));
        Assert.Equal(2025413, (int)stake.TokenId);
        Assert.Equal(Gauge, stake.Gauge);
        Assert.Equal(49, stake.FirstHop.LogIndex);
        Assert.Equal(59, stake.LastHop.LogIndex);
    }

    [Fact]
    public void OwnerTransferInTheTx_IsLeftToTheNormalPath()
    {
        var logs = new[]
        {
            Log(Npm, 1, Transfer, T(ZeroAddress), T(Owner), T(7)),
            Log(Npm, 2, Transfer, T(Owner), T(Gauge), T(7)),
            Log(Gauge, 3, Deposit, T(Owner), T(Owner), T(7))
        };

        Assert.Empty(UniswapV3StyleProvider.FindMediatedStakes(logs, Npm, Owner));
    }

    [Fact]
    public void NftEndingInContractThatDoesNotCreditOwner_IsIgnored()
    {
        var logs = new[]
        {
            Log(Npm, 1, Transfer, T(ZeroAddress), T(Zap), T(7)),
            Log(Npm, 2, Transfer, T(Zap), T(Gauge), T(7)),
            Log(Gauge, 3, Deposit, T(Zap), T(Zap), T(7))
        };

        Assert.Empty(UniswapV3StyleProvider.FindMediatedStakes(logs, Npm, Owner));
    }
}
