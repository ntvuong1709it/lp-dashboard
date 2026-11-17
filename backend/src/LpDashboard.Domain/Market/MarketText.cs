using System.Globalization;
using System.Net;

namespace LpDashboard.Domain.Market;

/// <summary>Cleaning and reading the strings Nasdaq sends: HTML-escaped text, "&amp;nbsp;" for "not published", values with units.</summary>
public static class MarketText
{
    /// <summary>Decodes HTML entities and trims. A non-breaking space, a blank or null becomes the empty string.</summary>
    public static string Clean(string? raw) => raw is null ? "" : WebUtility.HtmlDecode(raw).Trim();

    /// <summary>
    /// Reads "3.1%", "-0.4%", "$0.64", "1.380M", "19.80K", "$1,016,233,530,000". A suffix K, M, B or T multiplies.
    /// Blank, "N/A" and anything else unreadable is false, never an exception.
    /// </summary>
    public static bool TryParseValue(string? raw, out decimal value)
    {
        value = 0;
        var text = Clean(raw).Replace(",", "").Replace("$", "").TrimEnd('%').Trim();
        if (text.Length == 0) return false;

        var multiplier = 1m;
        switch (char.ToUpperInvariant(text[^1]))
        {
            case 'K': multiplier = 1_000m; break;
            case 'M': multiplier = 1_000_000m; break;
            case 'B': multiplier = 1_000_000_000m; break;
            case 'T': multiplier = 1_000_000_000_000m; break;
        }
        if (multiplier != 1m) text = text[..^1];

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        value = number * multiplier;
        return true;
    }
}
