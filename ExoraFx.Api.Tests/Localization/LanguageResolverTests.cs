using ExoraFx.Api.Localization;

namespace ExoraFx.Api.Tests.Localization;

public class LanguageResolverTests
{
    private readonly LanguageResolver _resolver = new();

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("uk", "uk")]
    [InlineData("en", "en")]
    [InlineData("RU", "ru")]
    [InlineData("EN", "en")]
    [InlineData("en-US", "en")]
    [InlineData("uk-UA", "uk")]
    [InlineData("ru-RU", "ru")]
    [InlineData("ua", "uk")]
    [InlineData("UA", "uk")]
    public void ResolveLanguage_ReturnsCanonicalCode(string input, string expected) =>
        Assert.Equal(expected, _resolver.ResolveLanguage(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("de-DE")]
    [InlineData("xx")]
    [InlineData("zh-CN")]
    public void ResolveLanguage_FallsBackToDefaultForUnknown(string? input) =>
        Assert.Equal(LanguageResolver.DefaultLanguage, _resolver.ResolveLanguage(input));

    [Theory]
    [InlineData("ru", true)]
    [InlineData("uk", true)]
    [InlineData("en", true)]
    [InlineData("ua", true)]
    [InlineData("UA", true)]
    [InlineData("RU", true)]
    [InlineData("fr", false)]
    [InlineData("en-US", false)]
    [InlineData("uk-UA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedInput_AcceptsBareCodesAndUaAlias(string? input, bool expected) =>
        Assert.Equal(expected, _resolver.IsSupportedInput(input));

    [Fact]
    public void ResolveLanguage_StripCaseAndRegion_Compounds()
    {
        Assert.Equal("uk", _resolver.ResolveLanguage("UA-Cyrl-UA"));
        Assert.Equal("en", _resolver.ResolveLanguage("EN-Latn-US"));
    }
}
