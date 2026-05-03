namespace ExoraFx.Api.Localization;

public interface ILanguageValidator
{
    string ResolveLanguage(string? languageCode);

    bool IsSupportedInput(string? languageCode);
}
