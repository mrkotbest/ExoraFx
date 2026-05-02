using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Services.Bot;

public sealed class BotLogService : IBotLogService
{
    private static readonly string Dir = EnsureLogDir();

    private readonly Lock _lock = new();
    private readonly ILogger<BotLogService> _logger;
    private readonly LoggingSettings _settings;

    public BotLogService(ILogger<BotLogService> logger, IOptions<LoggingSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
        TrySweep();
    }

    public void LogIn(long chatId, string? userName, long? userId, bool isAdmin, string? languageCode, string text)
    {
        var who = string.IsNullOrEmpty(userName) ? "—" : $"@{userName}";
        var adminTag = isAdmin ? " [ADMIN]" : "";
        var lang = string.IsNullOrEmpty(languageCode) ? "—" : languageCode;
        Append($"IN  {TimeHelper.NowKyiv()} chat={chatId} user={who}({userId}){adminTag} lang={lang}\n    {Indent(text)}\n");
    }

    public void LogOut(long chatId, string text) =>
        Append($"OUT {TimeHelper.NowKyiv()} chat={chatId}\n    {Indent(text)}\n\n");

    public void LogEvent(string message) =>
        Append($"EV  {TimeHelper.NowKyiv()} {Indent(message)}\n\n");

    private void Append(string entry)
    {
        try
        {
            var path = Path.Combine(Dir, $"bot-{TimeHelper.TodayKyiv():yyyy-MM-dd}.log");
            lock (_lock)
            {
                File.AppendAllText(path, entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot log append failed");
        }
    }

    private void TrySweep()
    {
        if (_settings.RetentionDays <= 0)
            return;

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);
            var deleted = 0;
            foreach (var file in Directory.EnumerateFiles(Dir, "bot-*.log"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete();
                    deleted++;
                }
            }

            if (deleted > 0)
                _logger.LogInformation("BotLog: removed {Count} stale chat-logs (retention {Days} days)", deleted, _settings.RetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BotLog sweep failed");
        }
    }

    private static string Indent(string text) => text.ReplaceLineEndings("\n    ");

    private static string EnsureLogDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "chat-logs");
        Directory.CreateDirectory(path);
        return path;
    }
}
