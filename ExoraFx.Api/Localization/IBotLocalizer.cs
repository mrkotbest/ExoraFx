namespace ExoraFx.Api.Localization;

public interface IBotLocalizer
{
    string Get(string key, string? languageCode, params object[] args);
}
