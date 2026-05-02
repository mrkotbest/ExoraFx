namespace ExoraFx.Api.Configuration;

public sealed class TelegramSettings
{
    public const string SectionName = "Telegram";

    public string BotToken { get; init; } = "";
    public List<long> Admins { get; init; } = [];

    public bool IsAdmin(long? userId) => userId is { } id && Admins.Contains(id);
}
