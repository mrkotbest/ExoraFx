using Telegram.Bot;

namespace ExoraFx.Api.Services.Bot;

public interface ITelegramBotClientProvider
{
    TelegramBotClient? Client { get; }
}
