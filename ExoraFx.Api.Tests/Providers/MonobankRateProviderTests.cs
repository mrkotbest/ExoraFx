using ExoraFx.Api.Configuration;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Providers;

public class MonobankRateProviderTests
{
    private static MonobankRateProvider Build(string responseJson)
    {
        var handler = new StubHttpMessageHandler(new Dictionary<string, string> { ["bank/currency"] = responseJson });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.monobank.ua/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Monobank")).Returns(client);

        var settings = new ExchangeSettings { SupportedCurrencies = ["eur", "usd", "pln"] };
        return new MonobankRateProvider(factory.Object, Options.Create(settings));
    }

    [Fact]
    public async Task ParsesEurUsdPlnAgainstUah()
    {
        var json =
            """
            [
              { "currencyCodeA": 978, "currencyCodeB": 980, "rateBuy": 47.10, "rateSell": 48.30, "rateCross": 47.7 },
              { "currencyCodeA": 840, "currencyCodeB": 980, "rateBuy": 41.00, "rateSell": 41.50 },
              { "currencyCodeA": 985, "currencyCodeB": 980, "rateBuy": 11.00, "rateSell": 11.30 }
            ]
            """;
        var provider = Build(json);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(47.10m, rates["eur"]);
        Assert.Equal(41.00m, rates["usd"]);
        Assert.Equal(11.00m, rates["pln"]);
    }

    [Fact]
    public async Task FallsBackToRateCrossWhenRateBuyMissing()
    {
        var json =
            """
            [
              { "currencyCodeA": 978, "currencyCodeB": 980, "rateCross": 47.7 }
            ]
            """;
        var provider = Build(json);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(47.7m, rates["eur"]);
    }

    [Fact]
    public async Task SkipsCurrenciesNotPairedWithUah()
    {
        var json =
            """
            [
              { "currencyCodeA": 978, "currencyCodeB": 840, "rateBuy": 1.10 }
            ]
            """;
        var provider = Build(json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRatesAsync());
    }

    [Fact]
    public async Task ThrowsWhenResponseHasNoSupportedCurrencies()
    {
        var provider = Build("[]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRatesAsync());
    }

    [Fact]
    public async Task ReturnsPartialResultsWhenSomeCurrenciesMissing()
    {
        var json =
            """
            [
              { "currencyCodeA": 978, "currencyCodeB": 980, "rateBuy": 47.10 }
            ]
            """;
        var provider = Build(json);

        var rates = await provider.GetRatesAsync();

        Assert.Single(rates);
        Assert.Equal(47.10m, rates["eur"]);
    }
}
