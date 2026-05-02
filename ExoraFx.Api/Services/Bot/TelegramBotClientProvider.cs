using ExoraFx.Api.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace ExoraFx.Api.Services.Bot;

public sealed class TelegramBotClientProvider(IOptions<TelegramSettings> settings) : ITelegramBotClientProvider
{
    public TelegramBotClient? Client { get; } = string.IsNullOrWhiteSpace(settings.Value.BotToken)
        ? null
        : new TelegramBotClient(settings.Value.BotToken);
}
