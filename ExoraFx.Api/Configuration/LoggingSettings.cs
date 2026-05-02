namespace ExoraFx.Api.Configuration;

public sealed class LoggingSettings
{
    public const string SectionName = "BotLogging";

    public int RetentionDays { get; init; } = 30;
}
