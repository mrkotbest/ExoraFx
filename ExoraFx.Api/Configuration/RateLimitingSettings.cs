namespace ExoraFx.Api.Configuration;

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
}
