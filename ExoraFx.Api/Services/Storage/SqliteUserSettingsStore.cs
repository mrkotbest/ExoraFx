using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using ExoraFx.Api.Persistence;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace ExoraFx.Api.Services.Storage;

public sealed class SqliteUserSettingsStore(
    IDbConnectionFactory factory,
    ILanguageValidator languageValidator,
    IOptions<ExchangeSettings> exchangeSettings) : IUserSettingsStore
{
    private readonly ExchangeSettings _exchange = exchangeSettings.Value;
    private readonly Lock _writeLock = new();

    public UserSettings Get(long? userId)
    {
        if (userId is not { } id)
            return UserSettings.Empty(0);

        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT user_name, user_role, language, margin_percent, default_bank, default_currency, default_amount, history_enabled, show_best_hint " +
            "FROM user_settings WHERE user_id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new UserSettings(
                id,
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetBoolean(8))
            : UserSettings.Empty(id);
    }

    public void RecordIdentity(long userId, string? userName, string userRole) =>
        Update(userId, current => current with { UserName = userName, UserRole = userRole });

    public bool TrySetLanguage(long userId, string language)
    {
        if (!languageValidator.IsSupportedInput(language))
            return false;

        var normalized = languageValidator.ResolveLanguage(language);
        Update(userId, current => current with { Language = normalized });
        return true;
    }

    public bool TrySetMargin(long userId, decimal percent)
    {
        if (!UserSettingsLimits.IsValidMargin(percent))
            return false;

        Update(userId, current => current with { MarginPercent = percent });
        return true;
    }

    public bool TrySetBank(long userId, string bank)
    {
        var normalized = CurrencyHelper.NormalizeBank(bank);
        if (!UserSettingsLimits.IsValidBank(normalized))
            return false;

        Update(userId, current => current with { DefaultBank = normalized });
        return true;
    }

    public bool TrySetCurrency(long userId, string currency)
    {
        var normalized = CurrencyHelper.Normalize(currency);
        if (normalized is null || normalized == CurrencyHelper.Uah || !_exchange.SupportedCurrencies.Contains(normalized))
            return false;

        Update(userId, current => current with { DefaultCurrency = normalized });
        return true;
    }

    public bool TrySetAmount(long userId, decimal amount)
    {
        if (amount <= 0)
            return false;

        Update(userId, current => current with { DefaultAmount = amount });
        return true;
    }

    public void SetHistoryEnabled(long userId, bool enabled) =>
        Update(userId, current => current with { HistoryEnabled = enabled });

    public void SetShowBestHint(long userId, bool enabled) =>
        Update(userId, current => current with { ShowBestHint = enabled });

    public void ResetField(long userId, UserSettingsField field)
    {
        Update(userId, current => field switch
        {
            UserSettingsField.Language => current with { Language = null },
            UserSettingsField.MarginPercent => current with { MarginPercent = null },
            UserSettingsField.DefaultBank => current with { DefaultBank = null },
            UserSettingsField.DefaultCurrency => current with { DefaultCurrency = null },
            UserSettingsField.DefaultAmount => current with { DefaultAmount = null },
            _ => current,
        });
    }

    public void Reset(long userId)
    {
        lock (_writeLock)
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE user_settings SET
                    language         = NULL,
                    margin_percent   = NULL,
                    default_bank     = NULL,
                    default_currency = NULL,
                    default_amount   = NULL,
                    history_enabled  = NULL,
                    show_best_hint   = NULL,
                    updated_at_utc   = $updated
                WHERE user_id = $id
                """;
            command.Parameters.AddWithValue("$id", userId);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    private void Update(long userId, Func<UserSettings, UserSettings> mutator)
    {
        lock (_writeLock)
        {
            var current = Get(userId);
            var updated = mutator(current);

            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO user_settings
                    (user_id, user_name, user_role, language, margin_percent, default_bank, default_currency, default_amount, history_enabled, show_best_hint, updated_at_utc)
                VALUES
                    ($id, $name, $role, $lang, $margin, $bank, $currency, $amount, $history, $bestHint, $updated)
                ON CONFLICT(user_id) DO UPDATE SET
                    user_name        = excluded.user_name,
                    user_role        = excluded.user_role,
                    language         = excluded.language,
                    margin_percent   = excluded.margin_percent,
                    default_bank     = excluded.default_bank,
                    default_currency = excluded.default_currency,
                    default_amount   = excluded.default_amount,
                    history_enabled  = excluded.history_enabled,
                    show_best_hint   = excluded.show_best_hint,
                    updated_at_utc   = excluded.updated_at_utc
                """;
            command.Parameters.AddWithValue("$id", userId);
            command.Parameters.AddWithValue("$name", (object?)updated.UserName ?? DBNull.Value);
            command.Parameters.AddWithValue("$role", (object?)updated.UserRole ?? DBNull.Value);
            command.Parameters.AddWithValue("$lang", (object?)updated.Language ?? DBNull.Value);
            command.Parameters.AddWithValue("$margin", (object?)updated.MarginPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("$bank", (object?)updated.DefaultBank ?? DBNull.Value);
            command.Parameters.AddWithValue("$currency", (object?)updated.DefaultCurrency ?? DBNull.Value);
            command.Parameters.AddWithValue("$amount", (object?)updated.DefaultAmount ?? DBNull.Value);
            command.Parameters.AddWithValue("$history", updated.HistoryEnabled is { } he ? (object)(he ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$bestHint", updated.ShowBestHint is { } bh ? (object)(bh ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }
}
