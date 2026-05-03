namespace ExoraFx.Api.Localization;

public sealed class LanguageResolver : ILanguageValidator
{
    public const string DefaultLanguage = "ru";

    private static readonly string[] Supported = ["ru", "uk", "en"];

    private static readonly Dictionary<string, string> InputAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ua"] = "uk",
    };

    public string ResolveLanguage(string? languageCode)
    {
        if (languageCode is null)
            return DefaultLanguage;

        var dash = languageCode.IndexOf('-');
        var prefix = (dash < 0 ? languageCode : languageCode[..dash]).ToLowerInvariant();

        if (InputAliases.TryGetValue(prefix, out var alias))
            return alias;

        return Array.IndexOf(Supported, prefix) >= 0 ? prefix : DefaultLanguage;
    }

    public bool IsSupportedInput(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var prefix = languageCode.Trim().ToLowerInvariant();
        return InputAliases.ContainsKey(prefix) || Array.IndexOf(Supported, prefix) >= 0;
    }
}
