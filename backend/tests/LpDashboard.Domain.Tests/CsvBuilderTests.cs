using System.Text;
using LpDashboard.Application.Export;

namespace LpDashboard.Domain.Tests;

public class CsvBuilderTests
{
    private static string Text(CsvBuilder csv) => new UTF8Encoding(false).GetString(csv.ToBytes()).TrimStart('﻿');

    [Fact]
    public void WritesHeaderAndRows_WithCrLf_AndUtf8Bom()
    {
        var csv = new CsvBuilder("a", "b").AddRow("x", 1);

        Assert.Equal("a,b\r\nx,1\r\n", Text(csv));
        Assert.Equal([0xEF, 0xBB, 0xBF], csv.ToBytes().Take(3));
    }

    [Fact]
    public void QuotesCellsWithCommasQuotesAndNewlines()
    {
        Assert.Equal("\"a,b\"", CsvBuilder.Format("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvBuilder.Format("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", CsvBuilder.Format("line1\nline2"));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    public void TextThatLooksLikeAFormula_IsDefused(string text)
    {
        Assert.StartsWith("'", CsvBuilder.Format(text).TrimStart('"'));
    }

    [Fact]
    public void NegativeNumbers_AreNotDefused()
    {
        Assert.Equal("-12.5", CsvBuilder.Format(-12.5m));
        Assert.Equal("-0.25", CsvBuilder.Format(-0.25));
    }

    [Fact]
    public void Decimals_DropTrailingZeros_AndIgnoreCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-CH");
        try
        {
            Assert.Equal("1.5", CsvBuilder.Format(1.500000m));
            Assert.Equal("1234.5678", CsvBuilder.Format(1234.5678m));
            Assert.Equal("0", CsvBuilder.Format(0.000m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DatesBoolsNullsAndEnums()
    {
        Assert.Equal("2026-10-01T12:30:00Z", CsvBuilder.Format(new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Unspecified)));
        Assert.Equal("2026-10-01", CsvBuilder.Format(new DateOnly(2026, 10, 1)));
        Assert.Equal("true", CsvBuilder.Format(true));
        Assert.Equal("", CsvBuilder.Format(null));
        Assert.Equal("Open", CsvBuilder.Format(PositionStatus.Open));
        Assert.Equal("", CsvBuilder.Format(double.NaN));
    }
}
