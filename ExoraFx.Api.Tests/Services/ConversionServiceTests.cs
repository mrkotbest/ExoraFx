using ExoraFx.Api.Configuration;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Services;

public class ConversionServiceTests
{
    private readonly ExchangeSettings _settings = new()
    {
        MarginPercent = 10m,
        CacheTtlSeconds = 300,
        DefaultBank = "monobank",
        SupportedCurrencies = ["eur", "usd"],
    };

    private ExchangeRateService CreateRates(params (string Bank, string Currency, decimal Rate)[] seed)
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

        var service = new ExchangeRateService(
            providers,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ExchangeRateService>.Instance);
        service.RefreshAllAsync().GetAwaiter().GetResult();

        return service;
    }

    private ConversionService CreateConverter(ExchangeRateService rates) =>
        new(rates, Options.Create(_settings));

    [Fact]
    public void Convert_ForeignToUah_AppliesMarginHaircut()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m);

        Assert.NotNull(result);
        Assert.Equal("EUR", result.From);
        Assert.Equal("UAH", result.To);
        Assert.Equal(4500m, result.ToAmount);
        Assert.Equal(500m, result.ProfitUah);
    }

    [Fact]
    public void Convert_UahToForeign_ClientGetsLessAtMarginHaircut()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("uah", "eur", 1000m);

        Assert.NotNull(result);
        Assert.Equal(18m, result.ToAmount);
        Assert.Equal(100m, result.ProfitUah);
    }

    [Fact]
    public void Convert_ForeignToForeign_PivotsThroughUah()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("monobank", "usd", 40m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "usd", 100m);

        Assert.NotNull(result);
        Assert.Equal(112.5m, result.ToAmount);
        Assert.Equal(500m, result.ProfitUah);
    }

    [Fact]
    public void Convert_UnknownCurrency_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("xxx", "uah", 100m));
        Assert.Null(convert.Convert("eur", "yyy", 100m));
    }

    [Fact]
    public void Convert_SameCurrency_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("eur", "eur", 100m));
    }

    [Fact]
    public void Convert_ZeroOrNegativeAmount_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("eur", "uah", 0m));
        Assert.Null(convert.Convert("eur", "uah", -5m));
    }

    [Fact]
    public void Convert_NoRateLoaded_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("pln", "uah", 100m));
    }

    [Fact]
    public void Convert_FallsBackToBestBank_WhenDefaultMissesCurrency()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("nbu", "pln", 12m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("pln", "uah", 100m);
        Assert.NotNull(result);
        Assert.Equal("nbu", result.Bank);
    }

    [Fact]
    public void Convert_HonoursMarginOverride()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, bankRaw: null, marginOverride: 0m);
        Assert.NotNull(result);
        Assert.Equal(5000m, result.ToAmount);
        Assert.Equal(0m, result.ProfitUah);
    }

    [Fact]
    public void Convert_PinnedBank_DoesNotFallBackWhenBankLacksCurrency()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("nbu", "pln", 12m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("pln", "uah", 100m, bankRaw: "monobank"));
    }

    [Fact]
    public void Convert_PinnedBank_RecordedInResult()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("privatbank", "eur", 51m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, bankRaw: "privat");
        Assert.NotNull(result);
        Assert.Equal("privatbank", result.Bank);
    }

    [Fact]
    public void Convert_AverageBank_UsesArithmeticMean()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("privatbank", "eur", 52m),
            ("nbu", "eur", 48m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, bankRaw: "average", marginOverride: 0m);

        Assert.NotNull(result);
        Assert.Equal(5000m, result.ToAmount);
        Assert.Equal("average", result.Bank);
    }

    [Fact]
    public void Convert_OverridingMargin_RecordsValueInResult()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, marginOverride: 25m);

        Assert.NotNull(result);
        Assert.Equal(25m, result.MarginPercent);
    }

    [Fact]
    public void Convert_OutputCurrencyAndAmount_AreUpperCaseAndRoundedToTwo()
    {
        var rates = CreateRates(("monobank", "eur", 50.7777m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m);

        Assert.NotNull(result);
        Assert.Equal("EUR", result.From);
        Assert.Equal("UAH", result.To);
        Assert.Equal(100m, result.FromAmount);
        Assert.Equal(2, decimal.GetBits(result.ToAmount)[3] >> 16 & 0xff);
    }

    [Fact]
    public void Convert_FreshRate_NotStale()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m);
        Assert.NotNull(result);
        Assert.False(result.IsStale);
        Assert.True(result.RateAgeSec < _settings.CacheTtlSeconds);
    }

    [Fact]
    public void Convert_CrossCurrency_BankLabelMergesWhenLegsDiffer()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("nbu", "usd", 40m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "usd", 100m);

        Assert.NotNull(result);
        Assert.Contains("monobank", result.Bank);
        Assert.Contains("nbu", result.Bank);
    }

    [Fact]
    public void Convert_ProfitEur_UseSameBankAsConversion()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("nbu", "eur", 49m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, bankRaw: "nbu");

        Assert.NotNull(result);
        Assert.Equal(490m, result.ProfitUah);
        Assert.Equal(Math.Round(490m / 49m, 2), result.ProfitEur);
    }

    [Fact]
    public void Convert_ProfitEur_NullWhenBankHasNoEur()
    {
        var rates = CreateRates(("monobank", "usd", 40m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("usd", "uah", 100m);

        Assert.NotNull(result);
        Assert.Null(result.ProfitEur);
    }

    [Fact]
    public void ConvertReverse_BasicCalculation_ReturnsRequiredForeign()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.ConvertReverse("eur", 5000m);

        Assert.NotNull(result);
        Assert.Equal("EUR", result.From);
        Assert.Equal("UAH", result.To);
        Assert.Equal(Math.Round(5000m / (50m * 0.9m), 2), result.FromAmount);
        Assert.Equal(5000m, result.ToAmount);
        Assert.Equal(50m, result.OfficialRate);
        Assert.Equal(45m, result.EffectiveRate);
        Assert.Equal(Math.Round(5000m * 0.1m / 0.9m, 2), result.ProfitUah);
    }

    [Fact]
    public void ConvertReverse_UnknownCurrency_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.ConvertReverse("xxx", 5000m));
    }

    [Fact]
    public void ConvertReverse_UahAsForeign_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.ConvertReverse("uah", 5000m));
    }

    [Fact]
    public void ConvertReverse_ZeroOrNegativeTarget_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.ConvertReverse("eur", 0m));
        Assert.Null(convert.ConvertReverse("eur", -100m));
    }

    [Fact]
    public void ConvertReverse_RateNotLoaded_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.ConvertReverse("pln", 5000m));
    }

    [Fact]
    public void ConvertReverse_PinnedBank_DoesNotFallBack()
    {
        var rates = CreateRates(
            ("monobank", "eur", 50m),
            ("nbu", "pln", 12m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.ConvertReverse("pln", 5000m, bankRaw: "monobank"));
    }

    [Fact]
    public void ConvertReverse_HonoursMarginOverride()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.ConvertReverse("eur", 5000m, marginOverride: 0m);

        Assert.NotNull(result);
        Assert.Equal(100m, result.FromAmount);
        Assert.Equal(0m, result.ProfitUah);
    }

    [Fact]
    public void Convert_MarginAtMaxBoundary_StillReturnsResult()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 100m, marginOverride: 50m);

        Assert.NotNull(result);
        Assert.Equal(50m, result.MarginPercent);
        Assert.Equal(25m, result.EffectiveRate);
    }

    [Fact]
    public void Convert_NegativeAmount_ReturnsNullOrZero()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("eur", "uah", -100m));
    }

    [Fact]
    public void Convert_VeryLargeAmount_NoOverflow()
    {
        var rates = CreateRates(("monobank", "eur", 40m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 1_000_000m);

        Assert.NotNull(result);
        Assert.Equal(1_000_000m * 40m * 0.9m, result.ToAmount);
    }

    [Fact]
    public void Convert_SameForeignToSameForeign_ReturnsNull()
    {
        var rates = CreateRates(("monobank", "eur", 50m));
        var convert = CreateConverter(rates);

        Assert.Null(convert.Convert("eur", "eur", 100m));
    }

    [Fact]
    public void Convert_CrossCurrency_RatesMatch_ProfitDeterministic()
    {
        var rates = CreateRates(
            ("monobank", "eur", 40m),
            ("monobank", "usd", 40m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "usd", 100m);

        Assert.NotNull(result);
        Assert.True(result.ProfitUah >= 0m);
    }

    [Fact]
    public void Convert_RoundingFloorsToCent()
    {
        var rates = CreateRates(("monobank", "eur", 41.7777m));
        var convert = CreateConverter(rates);

        var result = convert.Convert("eur", "uah", 1m);

        Assert.NotNull(result);
        Assert.Equal(2, BitConverter.GetBytes(decimal.GetBits(result.ToAmount)[3])[2]);
    }

    [Fact]
    public void ConvertReverse_FractionalUah_RoundsForeignAmount()
    {
        var rates = CreateRates(("monobank", "eur", 41.7777m));
        var convert = CreateConverter(rates);

        var result = convert.ConvertReverse("eur", 1m);

        Assert.NotNull(result);
        Assert.True(result.FromAmount > 0m);
        Assert.Equal(2, BitConverter.GetBytes(decimal.GetBits(result.FromAmount)[3])[2]);
    }
}
