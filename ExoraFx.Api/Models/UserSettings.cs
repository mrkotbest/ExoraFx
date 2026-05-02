namespace ExoraFx.Api.Models;

public sealed record UserSettings(
    long UserId,
    string? UserName,
    string? UserRole,
    string? Language,
    decimal? MarginPercent,
    string? DefaultBank,
    string? DefaultCurrency,
    decimal? DefaultAmount,
    bool? HistoryEnabled,
    bool? ShowBestHint)
{
    public static UserSettings Empty(long userId) => new(userId, null, null, null, null, null, null, null, null, null);
}

public enum UserSettingsField
{
    Language,
    MarginPercent,
    DefaultBank,
    DefaultCurrency,
    DefaultAmount,
}
