using ExoraFx.Api.Configuration;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Providers;

public class PrivatBankProviderTests
{
    private static PrivatBankProvider Build(string responseJson, IList<string>? currencies = null)
    {
        var handler = new StubHttpMessageHandler(new Dictionary<string, string> { ["pubinfo"] = responseJson });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.privatbank.ua/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("PrivatBank")).Returns(client);

        var settings = new ExchangeSettings { SupportedCurrencies = currencies?.ToList() ?? ["eur", "usd", "pln"] };
        return new PrivatBankProvider(factory.Object, Options.Create(settings));
    }

    [Fact]
    public async Task ParsesEurAndUsd()
    {
        var json =
            """
            [
              { "ccy": "EUR", "base_ccy": "UAH", "buy": "47.0500", "sale": "48.5000" },
              { "ccy": "USD", "base_ccy": "UAH", "buy": "41.0000", "sale": "41.6000" }
            ]
            """;
        var provider = Build(json);

        var rates = await provider.GetRatesAsync();

        Assert.Equal(47.05m, rates["eur"]);
        Assert.Equal(41.00m, rates["usd"]);
    }

    [Fact]
    public async Task IgnoresPlnEvenIfPresent()
    {
        var json =
            """
            [
              { "ccy": "EUR", "buy": "47.05", "sale": "48.5" },
              { "ccy": "PLN", "buy": "11.05", "sale": "11.30" }
            ]
            """;
        var provider = Build(json);

        var rates = await provider.GetRatesAsync();

        Assert.False(rates.ContainsKey("pln"));
    }

    [Fact]
    public async Task ThrowsWhenAllRatesUnparseable()
    {
        var json =
            """
            [
              { "ccy": "EUR", "buy": "n/a" },
              { "ccy": "USD", "buy": "" }
            ]
            """;
        var provider = Build(json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRatesAsync());
    }

    [Fact]
    public async Task ThrowsWhenResponseEmpty()
    {
        var provider = Build("[]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRatesAsync());
    }
}
