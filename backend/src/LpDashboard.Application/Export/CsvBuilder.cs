using System.Globalization;
using System.Text;

namespace LpDashboard.Application.Export;

/// <summary>
/// Minimal RFC 4180 CSV writer. Numbers and dates use fixed invariant formats so the file does not depend on the
/// server's culture; text cells that a spreadsheet would read as a formula get a leading apostrophe, because token
/// symbols come from the chain and anyone can deploy a token named "=HYPERLINK(...)".
/// </summary>
public sealed class CsvBuilder
{
    private readonly StringBuilder _text = new();

    public CsvBuilder(params string[] header) => AddRow(header);

    public CsvBuilder AddRow(params object?[] values)
    {
        _text.Append(string.Join(',', values.Select(Format))).Append("\r\n");
        return this;
    }

    /// <summary>UTF-8 with a byte order mark so Excel detects the encoding.</summary>
    public byte[] ToBytes()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(_text.ToString())];
    }

    public static string Format(object? value) => value switch
    {
        null => "",
        string s => Escape(GuardFormula(s)),
        // Dividing by 1.000...m drops trailing zeros without changing the value.
        decimal d => (d / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        double d => double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "",
        bool b => b ? "true" : "false",
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Escape(GuardFormula(value.ToString() ?? ""))
    };

    private static string GuardFormula(string s) =>
        s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;

    private static string Escape(string s) =>
        s.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
