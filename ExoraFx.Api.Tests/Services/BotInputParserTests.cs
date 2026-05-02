using ExoraFx.Api.Configuration;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Services;

public class BotInputParserTests
{
    private readonly ExchangeSettings _settings = new()
    {
        MarginPercent = 7m,
        CacheTtlSeconds = 300,
        DefaultBank = "monobank",
        SupportedCurrencies = ["eur", "usd", "pln"],
    };

    private BotInputParser CreateParser(params (string Bank, string Currency, decimal Rate)[] seed)
    {
        var providers = seed
            .GroupBy(s => s.Bank)
            .Select(g =>
            {
                var mock = new Mock<IRateProvider>();
                mock.Setup(p => p.Name).Returns(g.Key);
                mock.Setup(p => p.GetRatesAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(g.ToDictionary(x => x.Currency, x => x.Rate));
                return mock.Object;
            })
            .ToArray();

        var rateService = new ExchangeRateService(
            providers,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ExchangeRateService>.Instance);
        rateService.RefreshAllAsync().GetAwaiter().GetResult();

        var conversion = new ConversionService(rateService, Options.Create(_settings));
        var defaults = new Mock<IUserDefaultsResolver>();
        defaults.Setup(d => d.Margin(It.IsAny<long?>())).Returns(_settings.MarginPercent);
        defaults.Setup(d => d.Currency(It.IsAny<long?>())).Returns("eur");
        defaults.Setup(d => d.Amount(It.IsAny<long?>())).Returns(50m);
        defaults.Setup(d => d.Bank(It.IsAny<long?>())).Returns(_settings.DefaultBank);
        return new BotInputParser(conversion, defaults.Object);
    }

    [Fact]
    public void Parse_ForwardEurToUah_ReturnsSuccess()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("EUR", success.Result.From);
        Assert.Equal("UAH", success.Result.To);
        Assert.Equal(100m, success.Result.FromAmount);
        Assert.False(success.MarginOverridden);
    }

    [Fact]
    public void Parse_PinnedBank_RecordedInResult()
    {
        var parser = CreateParser(("monobank", "eur", 50m), ("privatbank", "eur", 51m));

        var outcome = parser.ParseCalculation("100 eur privat", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("privatbank", success.Result.Bank);
    }

    [Fact]
    public void Parse_ExplicitMarginPercent_FlagsOverridden()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur 8%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(8m, success.Result.MarginPercent);
        Assert.True(success.MarginOverridden);
    }

    [Fact]
    public void Parse_NoMargin_UsesUserMargin()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(_settings.MarginPercent, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_CrossEurToUsd_PivotsThroughUah()
    {
        var parser = CreateParser(
            ("monobank", "eur", 50m),
            ("monobank", "usd", 40m));

        var outcome = parser.ParseCalculation("100 eur usd", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("EUR", success.Result.From);
        Assert.Equal("USD", success.Result.To);
    }

    [Fact]
    public void Parse_UahWithoutTo_DefaultsToEur_Reverse()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("5000 uah", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("EUR", success.Result.From);
        Assert.Equal("UAH", success.Result.To);
        Assert.Equal(5000m, success.Result.ToAmount);
    }

    [Fact]
    public void Parse_UahToEur_Explicit_SameAsDefault()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var explicitOutcome = parser.ParseCalculation("5000 uah eur", userId: 1);
        var defaultOutcome = parser.ParseCalculation("5000 uah", userId: 1);

        var explicitOk = Assert.IsType<ParseOutcome.Success>(explicitOutcome);
        var defaultOk = Assert.IsType<ParseOutcome.Success>(defaultOutcome);
        Assert.Equal(explicitOk.Result.FromAmount, defaultOk.Result.FromAmount);
    }

    [Fact]
    public void Parse_BankAndMargin_BothApplied()
    {
        var parser = CreateParser(
            ("monobank", "eur", 50m),
            ("privatbank", "eur", 51m));

        var outcome = parser.ParseCalculation("100 eur mono 10%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("monobank", success.Result.Bank);
        Assert.Equal(10m, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_TokensAfterAmountInAnyOrder_ProducesSameResult()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var canonical = parser.ParseCalculation("100 eur mono 8%", userId: 1);
        var shuffled = parser.ParseCalculation("100 8% eur mono", userId: 1);

        var a = Assert.IsType<ParseOutcome.Success>(canonical);
        var b = Assert.IsType<ParseOutcome.Success>(shuffled);
        Assert.Equal(a.Result.FromAmount, b.Result.FromAmount);
        Assert.Equal(a.Result.MarginPercent, b.Result.MarginPercent);
        Assert.Equal(a.Result.Bank, b.Result.Bank);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsUnknownError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.UnknownBody, error.Key);
    }

    [Fact]
    public void Parse_AmountOnly_ReturnsUnknownError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.UnknownBody, error.Key);
    }

    [Fact]
    public void Parse_UnknownToken_ReturnsErrorWithToken()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 abc", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseUnknownToken, error.Key);
        Assert.Contains("abc", error.Args);
    }

    [Fact]
    public void Parse_ThreeCurrencies_ReturnsTooManyError()
    {
        var parser = CreateParser(
            ("monobank", "eur", 50m),
            ("monobank", "usd", 40m),
            ("monobank", "pln", 12m));

        var outcome = parser.ParseCalculation("100 eur usd pln", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseTooManyCurrencies, error.Key);
    }

    [Fact]
    public void Parse_SameCurrencyOnBothSides_ReturnsSameError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur eur", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseSameCurrency, error.Key);
    }

    [Fact]
    public void Parse_MarginAboveMax_ReturnsOutOfRangeError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur 60%", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseMarginOutOfRange, error.Key);
    }

    [Fact]
    public void Parse_NoRateLoaded_ReturnsRateNotLoadedError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 pln", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseRateNotLoaded, error.Key);
    }

    [Fact]
    public void Parse_LastMarginWins_WhenSpecifiedTwice()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur 8% 10%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(10m, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_CommaDecimalAmount_ParsesCorrectly()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("1,5 eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(1.5m, success.Result.FromAmount);
    }

    [Fact]
    public void Parse_NegativeAmount_ReturnsUnknownError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("-100 eur", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.UnknownBody, error.Key);
    }

    [Theory]
    [InlineData("5к", 5000)]
    [InlineData("5K", 5000)]
    [InlineData("5k", 5000)]
    [InlineData("1.5к", 1500)]
    [InlineData("1,5к", 1500)]
    [InlineData("0.5K", 500)]
    public void Parse_ThousandsShorthand_MultipliesByThousand(string input, decimal expected)
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation($"{input} eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(expected, success.Result.FromAmount);
    }

    [Fact]
    public void Parse_ForwardDirection_ReturnsForward()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(ConversionDirection.Forward, success.Direction);
    }

    [Fact]
    public void Parse_UahFirst_ReturnsReverseDirection()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("5000 uah", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(ConversionDirection.Reverse, success.Direction);
    }

    [Fact]
    public void Parse_TwoForeignCurrencies_ReturnsCrossDirection()
    {
        var parser = CreateParser(
            ("monobank", "eur", 50m),
            ("monobank", "usd", 40m));

        var outcome = parser.ParseCalculation("100 eur usd", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(ConversionDirection.Cross, success.Direction);
    }

    [Fact]
    public void Parse_BareForeignCurrencyToken_UsesDefaultAmount()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("евро", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(50m, success.Result.FromAmount);
        Assert.Equal("EUR", success.Result.From);
        Assert.Equal("UAH", success.Result.To);
    }

    [Fact]
    public void Parse_BareUahToken_TriggersReverseToDefaultCurrency()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("грн", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("EUR", success.Result.From);
        Assert.Equal("UAH", success.Result.To);
        Assert.Equal(50m, success.Result.ToAmount);
    }

    [Fact]
    public void Parse_ExplicitBank_BankExplicitFlagTrue()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur mono", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.True(success.BankExplicit);
    }

    [Fact]
    public void Parse_NoBankToken_BankExplicitFlagFalse()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.False(success.BankExplicit);
    }

    [Fact]
    public void Parse_ExtraInteriorWhitespace_TokenizedNormally()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100   eur     8%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(100m, success.Result.FromAmount);
        Assert.Equal(8m, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_TabAndNewlineSeparators_AreTreatedAsWhitespace()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100\teur\n8%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(8m, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_AmountAtZero_ReturnsUnknownErrorBecauseAmountInvalid()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("0 eur", userId: 1);

        Assert.IsType<ParseOutcome.Error>(outcome);
    }

    [Fact]
    public void Parse_PercentSignAsBareToken_ReturnsErrorOnUnknownToken()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 % eur", userId: 1);

        Assert.IsType<ParseOutcome.Error>(outcome);
    }

    [Fact]
    public void Parse_HugeAmount_OverDecimalMaxValue_ReturnsError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("9999999999999999999999999999999 eur", userId: 1);

        Assert.IsType<ParseOutcome.Error>(outcome);
    }

    [Fact]
    public void Parse_BankWithUnknownBankToken_FallsBackToError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur sber", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseUnknownToken, error.Key);
    }

    [Fact]
    public void Parse_TwoBankTokens_LastWins()
    {
        var parser = CreateParser(
            ("monobank", "eur", 50m),
            ("privatbank", "eur", 51m),
            ("nbu", "eur", 49m));

        var outcome = parser.ParseCalculation("100 eur mono privat", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("privatbank", success.Result.Bank);
    }

    [Fact]
    public void Parse_AbsurdDecimalSeparator_RejectedAsAmount()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("1.2.3 eur", userId: 1);

        Assert.IsType<ParseOutcome.Error>(outcome);
    }

    [Fact]
    public void Parse_FullyUppercaseRussian_NormalizedToken()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 ЕВРО", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal("EUR", success.Result.From);
    }

    [Fact]
    public void Parse_AmountAttachedToCurrency_ReturnsErrorBecauseTokenized()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100eur", userId: 1);

        Assert.IsType<ParseOutcome.Error>(outcome);
    }

    [Fact]
    public void Parse_MarginAtBoundary_Zero_AcceptsAndOverrides()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur 0%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(0m, success.Result.MarginPercent);
        Assert.True(success.MarginOverridden);
    }

    [Fact]
    public void Parse_MarginAtBoundary_Fifty_Accepts()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur 50%", userId: 1);

        var success = Assert.IsType<ParseOutcome.Success>(outcome);
        Assert.Equal(50m, success.Result.MarginPercent);
    }

    [Fact]
    public void Parse_NegativeMarginPercent_ReturnsOutOfRangeError()
    {
        var parser = CreateParser(("monobank", "eur", 50m));

        var outcome = parser.ParseCalculation("100 eur -5%", userId: 1);

        var error = Assert.IsType<ParseOutcome.Error>(outcome);
        Assert.Equal(BotKeys.ParseMarginOutOfRange, error.Key);
    }
}
