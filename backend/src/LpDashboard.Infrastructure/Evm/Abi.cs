using System.Globalization;
using System.Numerics;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace LpDashboard.Infrastructure.Evm;

/// <summary>Minimal ABI helpers for static types (uint/int/address/bool) plus string decoding.</summary>
internal static class Abi
{
    public static readonly BigInteger MaxUint128 = (BigInteger.One << 128) - 1;
    private static readonly BigInteger TwoPow255 = BigInteger.One << 255;
    private static readonly BigInteger TwoPow256 = BigInteger.One << 256;

    public static string Keccak(string text)
    {
        var input = Encoding.UTF8.GetBytes(text);
        var digest = new KeccakDigest(256);
        digest.BlockUpdate(input, 0, input.Length);
        var output = new byte[32];
        digest.DoFinal(output, 0);
        return Convert.ToHexStringLower(output);
    }

    public static string Selector(string signature) => "0x" + Keccak(signature)[..8];

    public static string Topic(string signature) => "0x" + Keccak(signature);

    public static string Word(BigInteger value)
    {
        if (value.Sign < 0) value += TwoPow256;
        var hex = value.ToString("x").TrimStart('0');
        return hex.PadLeft(64, '0');
    }

    public static string Word(string address) => Strip(address).ToLowerInvariant().PadLeft(64, '0');

    public static string Encode(string signature, params string[] words) => Selector(signature) + string.Concat(words);

    public static string TopicOf(BigInteger value) => "0x" + Word(value);

    public static string TopicOf(string address) => "0x" + Word(address);

    public static IReadOnlyList<string> Words(string? hex)
    {
        var data = Strip(hex ?? string.Empty);
        var words = new List<string>(data.Length / 64);
        for (var i = 0; i + 64 <= data.Length; i += 64)
        {
            words.Add(data.Substring(i, 64));
        }
        return words;
    }

    public static BigInteger ToUInt(string word) =>
        BigInteger.Parse("0" + Strip(word), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static BigInteger ToInt(string word)
    {
        var value = ToUInt(word);
        return value >= TwoPow255 ? value - TwoPow256 : value;
    }

    public static string ToAddress(string word) => "0x" + Strip(word)[^40..].ToLowerInvariant();

    public static long ToLong(string hex) => (long)ToUInt(hex);

    public static string ToHex(long value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>Decodes an ABI string return value; falls back to bytes32 for legacy tokens (e.g. MKR).</summary>
    public static string DecodeString(string? hex)
    {
        var data = Strip(hex ?? string.Empty);
        if (data.Length == 0) return string.Empty;

        if (data.Length == 64)
        {
            return FromHexBytes(data).TrimEnd('\0');
        }

        var words = Words(data);
        var offset = (int)ToUInt(words[0]) * 2;
        var length = (int)ToUInt(data.Substring(offset, 64)) * 2;
        return FromHexBytes(data.Substring(offset + 64, length));
    }

    private static string FromHexBytes(string hex) => Encoding.UTF8.GetString(Convert.FromHexString(hex));

    private static string Strip(string hex) => hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
}
