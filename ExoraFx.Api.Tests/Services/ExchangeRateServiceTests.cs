using ExoraFx.Api.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ExoraFx.Api.Tests.Services;

public class ExchangeRateServiceTests
{
    private static ExchangeRateService CreateService(params IRateProvider[] providers) =>
        new(providers, new MemoryCache(new MemoryCacheOptions()), NullLogger<ExchangeRateService>.Instance);

    private static Mock<IRateProvider> MockProvider(string name, decimal eur, decimal usd = 0)
    {
        var mock = new Mock<IRateProvider>();
        mock.Setup(p => p.Name).Returns(name);
        var rates = new Dictionary<string, decimal> { ["eur"] = eur };
        if (usd > 0)
            rates["usd"] = usd;

        mock.Setup(p => p.GetRatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rates);
        return mock;
    }

    private static Mock<IRateProvider> FailingProvider(string name)
    {
        var mock = new Mock<IRateProvider>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.GetRatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        return mock;
    }

    [Fact]
    public async Task RefreshAll_CachesRatesFromAllProviders()
    {
        var service = CreateService(
            MockProvider("monobank", 51.71m, 41.50m).Object,
            MockProvider("privatbank", 51.50m, 41.30m).Object,
            MockProvider("nbu", 51.35m, 41.20m).Object);

        await service.RefreshAllAsync();

        var eur = service.GetAllRates("eur");
        Assert.Equal(3, eur.Count);
        Assert.Equal(51.71m, eur["monobank"].Rate);

        var usd = service.GetAllRates("usd");
        Assert.Equal(3, usd.Count);
        Assert.Equal(41.50m, usd["monobank"].Rate);
    }

    [Fact]
    public async Task GetRate_ReturnsSpecificBankAndCurrency()
    {
        var service = CreateService(MockProvider("monobank", 51.71m, 41.50m).Object);

        await service.RefreshAllAsync();

        Assert.Equal(51.71m, service.GetRate("monobank", "eur")!.Rate);
        Assert.Equal(41.50m, service.GetRate("monobank", "usd")!.Rate);
        Assert.Null(service.GetRate("monobank", "gbp"));
        Assert.Null(service.GetRate("unknown", "eur"));
    }

    [Fact]
    public async Task GetRate_IsCaseInsensitive()
    {
        var service = CreateService(MockProvider("monobank", 51.71m).Object);
        await service.RefreshAllAsync();

        Assert.Equal(51.71m, service.GetRate("MONOBANK", "EUR")!.Rate);
    }

    [Fact]
    public async Task GetAllRates_OnlyReturnsRequestedCurrency()
    {
        var service = CreateService(MockProvider("monobank", 51.71m, 41.50m).Object);
        await service.RefreshAllAsync();

        var eur = service.GetAllRates("eur");
        Assert.Single(eur);
        Assert.True(eur.ContainsKey("monobank"));
    }

    [Fact]
    public void GetAllRates_NothingLoaded_ReturnsEmpty()
    {
        var service = CreateService();
        var rates = service.GetAllRates("eur");
        Assert.Empty(rates);
    }

    [Fact]
    public async Task RefreshAll_SkipsFailedProviders_KeepsSuccessful()
    {
        var service = CreateService(
            FailingProvider("monobank").Object,
            MockProvider("nbu", 51.35m).Object);

        await service.RefreshAllAsync();

        var eur = service.GetAllRates("eur");
        Assert.Single(eur);
        Assert.Equal(51.35m, eur["nbu"].Rate);
        Assert.Null(service.GetRate("monobank", "eur"));
    }

    [Fact]
    public async Task StaleCache_PreservesOldRatesWhenProviderFails()
    {
        var mono = MockProvider("monobank", 51.71m);
        var service = CreateService(mono.Object);

        await service.RefreshAllAsync();
        Assert.Equal(51.71m, service.GetRate("monobank", "eur")!.Rate);

        mono.Setup(p => p.GetRatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        await service.RefreshAllAsync();

        Assert.Equal(51.71m, service.GetRate("monobank", "eur")!.Rate);
    }

    [Fact]
    public async Task GetHealth_ReportsProviderStatus()
    {
        var service = CreateService(
            MockProvider("monobank", 51.71m).Object,
            FailingProvider("nbu").Object);

        await service.RefreshAllAsync();

        var health = service.GetHealth();
        Assert.Equal("degraded", health.Status);
        Assert.Equal("ok", health.Banks.First(b => b.Name == "monobank").Status);
        Assert.Equal("error", health.Banks.First(b => b.Name == "nbu").Status);
    }

    [Fact]
    public async Task GetHealth_AllProvidersOk_ReportsHealthy()
    {
        var service = CreateService(
            MockProvider("monobank", 51.71m).Object,
            MockProvider("nbu", 51.35m).Object);

        await service.RefreshAllAsync();

        var health = service.GetHealth();
        Assert.Equal("healthy", health.Status);
        Assert.All(health.Banks, b => Assert.Equal("ok", b.Status));
    }

    [Fact]
    public void GetHealth_BeforeAnyRefresh_AllProvidersError()
    {
        var service = CreateService(MockProvider("monobank", 51.71m).Object);
        var health = service.GetHealth();

        Assert.Equal("degraded", health.Status);
        Assert.Equal("error", health.Banks[0].Status);
        Assert.Null(health.LastRefreshAt);
    }

    [Fact]
    public async Task GetHealth_AfterRefresh_RecordsLastRefreshAt()
    {
        var service = CreateService(MockProvider("monobank", 51.71m).Object);
        await service.RefreshAllAsync();

        var health = service.GetHealth();
        Assert.NotNull(health.LastRefreshAt);
    }
}
