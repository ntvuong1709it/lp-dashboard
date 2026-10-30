using System.Numerics;
using LpDashboard.Application.Addresses;
using LpDashboard.Infrastructure.Evm;

namespace LpDashboard.Domain.Tests;

public class AbiAndAddressTests
{
    [Fact]
    public void Keccak_TransferTopic_MatchesKnownHash() =>
        Assert.Equal("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef", Abi.Topic("Transfer(address,address,uint256)"));

    [Fact]
    public void Selector_BalanceOf_MatchesKnownSelector() =>
        Assert.Equal("0x70a08231", Abi.Selector("balanceOf(address)"));

    [Fact]
    public void NegativeInt_RoundTrips()
    {
        var word = Abi.Word(new BigInteger(-887272));
        Assert.Equal(64, word.Length);
        Assert.Equal(-887272, (int)Abi.ToInt(word));
    }

    [Fact]
    public void DecodeString_DynamicString()
    {
        var hex = "0x"
            + "0000000000000000000000000000000000000000000000000000000000000020"
            + "0000000000000000000000000000000000000000000000000000000000000004"
            + "5553444300000000000000000000000000000000000000000000000000000000";
        Assert.Equal("USDC", Abi.DecodeString(hex));
    }

    [Fact]
    public void DecodeString_Bytes32Fallback() =>
        Assert.Equal("MKR", Abi.DecodeString("0x4d4b520000000000000000000000000000000000000000000000000000000000"));

    [Fact]
    public void ToAddress_TakesLast20Bytes() =>
        Assert.Equal("0x4200000000000000000000000000000000000006", Abi.ToAddress("0000000000000000000000004200000000000000000000000000000000000006"));

    [Theory]
    [InlineData("0xAbCdEf0123456789abcdef0123456789ABCDEF01", "0xabcdef0123456789abcdef0123456789abcdef01", ChainFamily.Evm)]
    [InlineData("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", ChainFamily.Solana)]
    public void AddressParser_NormalizesValidAddresses(string input, string expected, ChainFamily family) =>
        Assert.Equal((expected, family), AddressParser.TryParse(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x123")]
    [InlineData("not an address")]
    [InlineData("0xZZcdef0123456789abcdef0123456789abcdef01")]
    public void AddressParser_RejectsInvalid(string? input) =>
        Assert.Null(AddressParser.TryParse(input));
}
