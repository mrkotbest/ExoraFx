using ExoraFx.Api.Configuration;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;
using Moq;

namespace ExoraFx.Api.Tests.Services;

public class UserDefaultsResolverTests
{
    private readonly ExchangeSettings _exchange = new()
    {
        MarginPercent = 7.7m,
        DefaultBank = "monobank",
        SupportedCurrencies = ["eur", "usd", "pln"],
    };

    private (UserDefaultsResolver Resolver, Mock<IUserSettingsStore> Store) CreateResolver(UserSettings? stored = null)
    {
        var store = new Mock<IUserSettingsStore>();
        store.Setup(s => s.Get(It.IsAny<long?>())).Returns(stored ?? UserSettings.Empty(0));
        return (new UserDefaultsResolver(store.Object, Options.Create(_exchange)), store);
    }

    [Fact]
    public void Margin_NoUserOverride_ReturnsExchangeDefault()
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal(7.7m, resolver.Margin(1L));
    }

    [Fact]
    public void Margin_WithUserOverride_ReturnsOverride()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, 12m, null, null, null, null, null));

        Assert.Equal(12m, resolver.Margin(1L));
    }

    [Fact]
    public void Margin_OverrideOutOfRange_ClampedToValidRange()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, 99m, null, null, null, null, null));

        Assert.Equal(50m, resolver.Margin(1L));
    }

    [Fact]
    public void Bank_NoOverride_ReturnsExchangeDefault()
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal("monobank", resolver.Bank(1L));
    }

    [Fact]
    public void Bank_WithOverride_ReturnsOverride()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, null, "privatbank", null, null, null, null));

        Assert.Equal("privatbank", resolver.Bank(1L));
    }

    [Fact]
    public void Currency_NoOverride_FallsBackToEur()
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal("eur", resolver.Currency(1L));
    }

    [Fact]
    public void Currency_WithOverride_ReturnsOverride()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, null, null, "usd", null, null, null));

        Assert.Equal("usd", resolver.Currency(1L));
    }

    [Fact]
    public void Amount_NoOverride_FallsBackToHundred()
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal(100m, resolver.Amount(1L));
    }

    [Fact]
    public void Amount_WithOverride_ReturnsOverride()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, null, null, null, 123m, null, null));

        Assert.Equal(123m, resolver.Amount(1L));
    }

    [Fact]
    public void Language_NoOverride_ReturnsNull()
    {
        var (resolver, _) = CreateResolver();

        Assert.Null(resolver.Language(1L));
    }

    [Fact]
    public void Language_WithOverride_ReturnsOverride()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, "uk", null, null, null, null, null, null));

        Assert.Equal("uk", resolver.Language(1L));
    }

    [Fact]
    public void ShowBestHint_NoOverride_DefaultsToTrue()
    {
        var (resolver, _) = CreateResolver();
        Assert.True(resolver.ShowBestHint(1L));
    }

    [Fact]
    public void ShowBestHint_OverrideFalse_ReturnsFalse()
    {
        var (resolver, _) = CreateResolver(new UserSettings(1L, null, null, null, null, null, null, null, null, false));
        Assert.False(resolver.ShowBestHint(1L));
    }
}
