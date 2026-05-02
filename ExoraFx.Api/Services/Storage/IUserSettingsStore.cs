using ExoraFx.Api.Models;

namespace ExoraFx.Api.Services.Storage;

public interface IUserSettingsStore
{
    UserSettings Get(long? userId);

    void RecordIdentity(long userId, string? userName, string userRole);

    bool TrySetLanguage(long userId, string language);

    bool TrySetMargin(long userId, decimal percent);

    bool TrySetBank(long userId, string bank);

    bool TrySetCurrency(long userId, string currency);

    bool TrySetAmount(long userId, decimal amount);

    void SetHistoryEnabled(long userId, bool enabled);

    void SetShowBestHint(long userId, bool enabled);

    void ResetField(long userId, UserSettingsField field);

    void Reset(long userId);
}
