using ExoraFx.Api.Configuration;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Providers;

public class NbuRateProviderTests
{
    private static NbuRateProvider Build(Dictionary<string, string> responses, IList<string>? currencies = null)
    {
        var handler = new StubHttpMessageHandler(responses);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://bank.gov.ua/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Nbu")).Returns(client);

        var settings = new ExchangeSettings { SupportedCurrencies = currencies?.ToList() ?? ["eur", "usd", "pln"] };
        return new NbuRateProvider(factory.Object, Options.Create(settings), NullLogger<NbuRateProvider>.Instance);
    }

    [Fact]
    public async Task ParsesAllSupportedCurrencies()
    {
        var responses = new Dictionary<string, string>
        {
            ["valcode=EUR"] = "[{\"r030\":978,\"txt\":\"Євро\",\"rate\":45.50,\"cc\":\"EUR\",\"exchangedate\":\"01.01.2026\"}]",
            ["valcode=USD"] = "[{\"r030\":840,\"txt\":\"Долар США\",\"rate\":40.00,\"cc\":\"USD\",\"exchangedate\":\"01.01.2026\"}]",
            ["valcode=PLN"] = "[{\"r030\":985,\"txt\":\"Злотий\",\"rate\":10.50,\"cc\":\"PLN\",\"exchangedate\":\"01.01.2026\"}]",
        };
        var provider = Build(responses);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(45.50m, rates["eur"]);
        Assert.Equal(40.00m, rates["usd"]);
        Assert.Equal(10.50m, rates["pln"]);
    }

    [Fact]
    public async Task ReturnsPartialResultsWhenOneFails()
    {
        var responses = new Dictionary<string, string>
        {
            ["valcode=EUR"] = "[{\"rate\":45.50}]",
            ["valcode=USD"] = "[{\"rate\":40.00}]",
            ["valcode=PLN"] = "garbage-not-json",
        };
        var provider = Build(responses);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(45.50m, rates["eur"]);
        Assert.Equal(40.00m, rates["usd"]);
        Assert.False(rates.ContainsKey("pln"));
    }

    [Fact]
    public async Task ThrowsWhenAllCurrenciesFailed()
    {
        var responses = new Dictionary<string, string>
        {
            ["valcode=EUR"] = "garbage",
            ["valcode=USD"] = "garbage",
            ["valcode=PLN"] = "garbage",
        };
        var provider = Build(responses);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRatesAsync());
    }

    [Fact]
    public async Task SkipsZeroRate()
    {
        var responses = new Dictionary<string, string>
        {
            ["valcode=EUR"] = "[{\"rate\":0}]",
            ["valcode=USD"] = "[{\"rate\":40.00}]",
            ["valcode=PLN"] = "[]",
        };
        var provider = Build(responses);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(40.00m, rates["usd"]);
        Assert.False(rates.ContainsKey("eur"));
        Assert.False(rates.ContainsKey("pln"));
    }
}
