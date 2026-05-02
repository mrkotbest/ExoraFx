using ExoraFx.Api.Helpers;

namespace ExoraFx.Api.Tests.Helpers;

public class CurrencyHelperTests
{
    [Theory]
    [InlineData("eur", "eur")]
    [InlineData("EUR", "eur")]
    [InlineData(" euro ", "eur")]
    [InlineData("e", "eur")]
    [InlineData("е", "eur")]
    [InlineData("є", "eur")]
    [InlineData("ев", "eur")]
    [InlineData("евро", "eur")]
    [InlineData("євро", "eur")]
    [InlineData("€", "eur")]
    [InlineData("usd", "usd")]
    [InlineData("d", "usd")]
    [InlineData("д", "usd")]
    [InlineData("долар", "usd")]
    [InlineData("долари", "usd")]
    [InlineData("доллар", "usd")]
    [InlineData("бакс", "usd")]
    [InlineData("$", "usd")]
    [InlineData("pln", "pln")]
    [InlineData("pl", "pln")]
    [InlineData("z", "pln")]
    [InlineData("з", "pln")]
    [InlineData("zł", "pln")]
    [InlineData("zloty", "pln")]
    [InlineData("злотый", "pln")]
    [InlineData("злотих", "pln")]
    [InlineData("uah", "uah")]
    [InlineData("грн", "uah")]
    [InlineData("гривна", "uah")]
    [InlineData("гривня", "uah")]
    [InlineData("hryvnia", "uah")]
    [InlineData("₴", "uah")]
    public void Normalize_ReturnsCanonicalCode(string input, string expected) =>
        Assert.Equal(expected, CurrencyHelper.Normalize(input));

    [Theory]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("rubble")]
    [InlineData("gbp")]
    [InlineData("chf")]
    [InlineData("czk")]
    public void Normalize_ReturnsNullForUnknown(string? input) =>
        Assert.Null(CurrencyHelper.Normalize(input));

    [Theory]
    [InlineData("mono", "monobank")]
    [InlineData("Monobank", "monobank")]
    [InlineData("приват", "privatbank")]
    [InlineData("nbu", "nbu")]
    [InlineData("НБУ", "nbu")]
    [InlineData("avg", "average")]
    [InlineData("среднее", "average")]
    public void NormalizeBank_ReturnsCanonicalName(string input, string expected) =>
        Assert.Equal(expected, CurrencyHelper.NormalizeBank(input));

    [Theory]
    [InlineData("somebank")]
    [InlineData("xxx")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeBank_ReturnsNullForUnknown(string? input) =>
        Assert.Null(CurrencyHelper.NormalizeBank(input));

    [Theory]
    [InlineData(100.0, 0, "100")]
    [InlineData(100.50, 2, "100.50")]
    [InlineData(100.1234, 4, "100.1234")]
    [InlineData(0, 2, "0")]
    [InlineData(7.7, 2, "7.70")]
    [InlineData(-5.25, 2, "-5.25")]
    [InlineData(1234567.89, 2, "1234567.89")]
    public void FormatAmount_FormatsCorrectly(decimal value, int decimals, string expected) =>
        Assert.Equal(expected, CurrencyHelper.FormatAmount(value, decimals));

    [Theory]
    [InlineData("eur", "🇪🇺")]
    [InlineData("usd", "🇺🇸")]
    [InlineData("pln", "🇵🇱")]
    [InlineData("uah", "🇺🇦")]
    [InlineData("EUR", "🇪🇺")]
    [InlineData("xxx", "")]
    [InlineData("", "")]
    public void Flag_ReturnsExpectedEmoji(string currency, string expected) =>
        Assert.Equal(expected, CurrencyHelper.Flag(currency));

    [Theory]
    [InlineData("eur", "€")]
    [InlineData("usd", "$")]
    [InlineData("pln", "zł")]
    [InlineData("uah", "₴")]
    [InlineData("USD", "$")]
    [InlineData("xxx", "")]
    public void Symbol_ReturnsExpectedGlyph(string currency, string expected) =>
        Assert.Equal(expected, CurrencyHelper.Symbol(currency));

    [Fact]
    public void Uah_Constant_IsLowercase() =>
        Assert.Equal("uah", CurrencyHelper.Uah);

    [Theory]
    [InlineData("8.5", 8.5)]
    [InlineData("8,5", 8.5)]
    [InlineData("100", 100)]
    [InlineData("0", 0)]
    public void TryParseDecimal_HandlesCommaAndDot(string token, double expected)
    {
        Assert.True(CurrencyHelper.TryParseDecimal(token, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void TryParseDecimal_RejectsNonNumeric(string token) =>
        Assert.False(CurrencyHelper.TryParseDecimal(token, out _));

    [Theory]
    [InlineData("8.5%", 8.5)]
    [InlineData("12%", 12)]
    [InlineData("0%", 0)]
    public void TryParsePercent_AcceptsTrailingPercent(string token, double expected)
    {
        Assert.True(CurrencyHelper.TryParsePercent(token, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void TryParsePercent_AcceptsNumberWithoutPercent() =>
        Assert.True(CurrencyHelper.TryParsePercent("8.5", out _));

    [Theory]
    [InlineData("100", 100)]
    [InlineData("5к", 5000)]
    [InlineData("5K", 5000)]
    [InlineData("1.5к", 1500)]
    [InlineData("1,5k", 1500)]
    public void TryParseAmount_AppliesKMultiplier(string token, double expected)
    {
        Assert.True(CurrencyHelper.TryParseAmount(token, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("abc")]
    public void TryParseAmount_RejectsNonPositive(string token) =>
        Assert.False(CurrencyHelper.TryParseAmount(token, out _));

    [Theory]
    [InlineData("к")]
    [InlineData("К")]
    [InlineData("k")]
    [InlineData("K")]
    public void TryParseAmount_BareSuffix_RejectsBecauseValueMissing(string token) =>
        Assert.False(CurrencyHelper.TryParseAmount(token, out _));

    [Fact]
    public void TryParseAmount_DecimalWithMultipleSeparators_RejectsCleanly()
    {
        Assert.False(CurrencyHelper.TryParseAmount("1.2.3", out _));
        Assert.False(CurrencyHelper.TryParseAmount("1,2,3", out _));
    }

    [Theory]
    [InlineData("0.001к", 1)]
    [InlineData("999999.99к", 999999990)]
    public void TryParseAmount_HandlesEdgeMagnitudes(string token, double expected)
    {
        Assert.True(CurrencyHelper.TryParseAmount(token, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void TryParseAmount_LeadingPlus_ReturnsTrue()
    {
        Assert.True(CurrencyHelper.TryParseAmount("+50", out var value));
        Assert.Equal(50m, value);
    }

    [Fact]
    public void TryParseAmount_DecimalMaxValue_DoesNotOverflowSilently()
    {
        Assert.True(CurrencyHelper.TryParseAmount(decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), out var value));
        Assert.Equal(decimal.MaxValue, value);
    }

    [Theory]
    [InlineData("8.5к", 8500)]
    [InlineData("0.5к", 500)]
    public void TryParseAmount_FractionalK_Preserved(string token, double expected)
    {
        Assert.True(CurrencyHelper.TryParseAmount(token, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("abc%")]
    [InlineData("%")]
    public void TryParsePercent_RejectsMalformed(string token) =>
        Assert.False(CurrencyHelper.TryParsePercent(token, out _));

    [Fact]
    public void TryParsePercent_DoublePercent_ParsedAsLeadingDigits()
    {
        Assert.True(CurrencyHelper.TryParsePercent("8%%", out var value));
        Assert.Equal(8m, value);
    }

    [Fact]
    public void Normalize_TrimAndCaseFolds_AcrossUnicodeAndSpaces()
    {
        Assert.Equal("eur", CurrencyHelper.Normalize("  EuRo  "));
        Assert.Equal("uah", CurrencyHelper.Normalize("\tГРН \n"));
    }

    [Fact]
    public void Normalize_CyrillicLatinHomoglyphs_OnlyExplicitAliasesAccepted()
    {
        Assert.Null(CurrencyHelper.Normalize("eвro"));
        Assert.Null(CurrencyHelper.Normalize("дoлap"));
    }

    [Theory]
    [InlineData(0, 2, "0")]
    [InlineData(0.001, 2, "0")]
    [InlineData(0.0049, 3, "0.005")]
    [InlineData(0.005, 3, "0.005")]
    [InlineData(0.0001, 3, "0")]
    public void FormatAmount_RoundingBoundaries(decimal value, int decimals, string expected) =>
        Assert.Equal(expected, CurrencyHelper.FormatAmount(value, decimals));

    [Fact]
    public void FormatAmount_NegativeZero_ProducesPlainZero() =>
        Assert.Equal("0", CurrencyHelper.FormatAmount(-0.0001m, 2));
}
