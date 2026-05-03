using ExoraFx.Api.Localization;

namespace ExoraFx.Api.Tests.Localization;

public class BotLocalizerTests
{
    private readonly BotLocalizer _localizer = new(new LanguageResolver());

    [Fact]
    public void Get_ReturnsRussianForNullLanguage()
    {
        var result = _localizer.Get(BotKeys.WhoamiRoleAdmin, null);
        Assert.Equal("админ", result);
    }

    [Theory]
    [InlineData("ru", "админ")]
    [InlineData("uk", "адмін")]
    [InlineData("ua", "адмін")]
    [InlineData("en", "admin")]
    public void Get_ReturnsLanguageSpecificValue(string lang, string expected) =>
        Assert.Equal(expected, _localizer.Get(BotKeys.WhoamiRoleAdmin, lang));

    [Fact]
    public void Get_FormatsArgsWithInvariantCulture()
    {
        var result = _localizer.Get(BotKeys.RatesTitle, "ru", 7.7m);
        Assert.Equal("*Курсы* (маржа 7.7%)", result);
    }

    [Fact]
    public void Get_MultipleArgs_AllSubstituted()
    {
        var result = _localizer.Get(BotKeys.WhoamiBody, "en", 12345L, "@user", "admin");
        Assert.Contains("12345", result);
        Assert.Contains("@user", result);
        Assert.Contains("admin", result);
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKeyAsIs()
    {
        var result = _localizer.Get("non.existent.key", "ru");
        Assert.Equal("non.existent.key", result);
    }

    [Fact]
    public void Get_KnownKeyWithoutArgs_ReturnsTemplateLiteral()
    {
        var result = _localizer.Get(BotKeys.ParseSameCurrency, "en");
        Assert.Equal("Same currency — nothing to convert.", result);
    }

    [Fact]
    public void Get_RegionStripped_BeforeLookup()
    {
        var withRegion = _localizer.Get(BotKeys.KbHelp, "uk-UA");
        var bare = _localizer.Get(BotKeys.KbHelp, "uk");
        Assert.Equal(bare, withRegion);
    }

    [Fact]
    public void Get_AllSupportedLangs_HaveDistinctKeyboardLabels()
    {
        var ru = _localizer.Get(BotKeys.KbRates, "ru");
        var uk = _localizer.Get(BotKeys.KbRates, "uk");
        var en = _localizer.Get(BotKeys.KbRates, "en");

        Assert.Contains("Курсы", ru);
        Assert.Contains("Курси", uk);
        Assert.Contains("Rates", en);
    }

    [Fact]
    public void Get_NumericFormatting_DoesNotUseLocaleSeparator()
    {
        var result = _localizer.Get(BotKeys.RatesTitle, "ru", 1234.5m);
        Assert.DoesNotContain(",", result);
        Assert.Contains("1234.5", result);
    }

    [Fact]
    public void Get_UaInput_ReturnsUkrainianTranslation()
    {
        var result = _localizer.Get(BotKeys.WhoamiRoleUser, "ua");
        Assert.Equal("користувач", result);
    }

    [Fact]
    public void Get_EveryDeclaredBotKey_HasNonKeyTranslationInAllLanguages()
    {
        var keyConstants = typeof(BotKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(keyConstants);

        foreach (var key in keyConstants)
        {
            foreach (var lang in new[] { "ru", "uk", "en" })
            {
                var translated = _localizer.Get(key, lang);
                Assert.False(translated == key, $"Key '{key}' has no translation in '{lang}'.");
            }
        }
    }

    [Fact]
    public void Get_ArgumentMismatch_DoesNotThrow_ButReturnsTemplate()
    {
        var noArgs = _localizer.Get(BotKeys.RatesTitle, "ru");
        Assert.Contains("{0}", noArgs);
    }
}
