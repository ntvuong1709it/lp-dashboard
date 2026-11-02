using System.Globalization;
using System.Numerics;
using System.Text;
using Nethereum.Util;

namespace LpLedger.Infrastructure.Chain;

/// <summary>Minimal ABI encoding for the few static-typed calls and logs the Aerodrome reader needs.</summary>
public static class Abi
{
    public static string Keccak(string text) => Sha3Keccack.Current.CalculateHash(text);

    public static string Selector(string signature) => "0x" + Keccak(signature)[..8];

    public static string Topic(string signature) => "0x" + Keccak(signature);

    public static string Word(BigInteger value) => value.ToString("x").TrimStart('0').PadLeft(64, '0');

    public static string AddressWord(string address) => address[2..].ToLowerInvariant().PadLeft(64, '0');

    public static string Call(string signature, params string[] words) => Selector(signature) + string.Concat(words);

    /// <summary>Splits ABI return data (with or without 0x) into 32-byte words.</summary>
    public static IReadOnlyList<string> Words(string hex)
    {
        var h = hex.StartsWith("0x", StringComparison.Ordinal) ? hex[2..] : hex;
        var words = new List<string>(h.Length / 64);
        for (var i = 0; i + 64 <= h.Length; i += 64) words.Add(h.Substring(i, 64));
        return words;
    }

    public static BigInteger ToUInt(string word) => BigInteger.Parse("0" + word, NumberStyles.AllowHexSpecifier);

    public static BigInteger ToInt(string word)
    {
        var v = ToUInt(word);
        return v >= BigInteger.One << 255 ? v - (BigInteger.One << 256) : v;
    }

    public static string ToAddress(string word) => "0x" + word[24..];

    /// <summary>Decodes a returned string, or a bytes32 (as old tokens like MKR return).</summary>
    public static string DecodeString(string hex)
    {
        var h = hex.StartsWith("0x", StringComparison.Ordinal) ? hex[2..] : hex;
        if (h.Length == 64) return Encoding.UTF8.GetString(Convert.FromHexString(h)).TrimEnd('\0');
        var offset = (int)ToUInt(h[..64]) * 2;
        var length = (int)ToUInt(h.Substring(offset, 64));
        return Encoding.UTF8.GetString(Convert.FromHexString(h.Substring(offset + 64, length * 2)));
    }
}
