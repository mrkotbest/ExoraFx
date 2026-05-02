namespace ExoraFx.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "DefaultCors";

    public List<string> AllowedOrigins { get; init; } = ["*"];
}
