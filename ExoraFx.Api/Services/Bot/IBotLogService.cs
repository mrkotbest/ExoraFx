namespace ExoraFx.Api.Services.Bot;

public interface IBotLogService
{
    void LogIn(long chatId, string? userName, long? userId, bool isAdmin, string? languageCode, string text);

    void LogOut(long chatId, string text);

    void LogEvent(string message);
}
