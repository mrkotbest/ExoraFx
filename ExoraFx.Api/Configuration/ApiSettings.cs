namespace ExoraFx.Api.Configuration;

public sealed class ApiSettings
{
    public const string SectionName = "Api";

    public string Key { get; init; } = "";
    public string HeaderName { get; init; } = "X-API-Key";
}
