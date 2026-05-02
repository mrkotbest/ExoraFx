using Microsoft.Data.Sqlite;

namespace ExoraFx.Api.Persistence;

public sealed class SchemaInitializer(IDbConnectionFactory factory, ILogger<SchemaInitializer> logger)
{
    private const string CreateUserSettings =
        """
        CREATE TABLE IF NOT EXISTS user_settings (
            user_id          INTEGER PRIMARY KEY,
            user_name        TEXT    NULL,
            user_role        TEXT    NULL,
            language         TEXT    NULL,
            margin_percent   REAL    NULL,
            default_bank     TEXT    NULL,
            default_currency TEXT    NULL,
            default_amount   REAL    NULL,
            history_enabled  INTEGER NULL,
            show_best_hint   INTEGER NULL,
            updated_at_utc   TEXT    NOT NULL
        )
        """;

    private const string CreateConversionHistory =
        """
        CREATE TABLE IF NOT EXISTS conversion_history (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id          INTEGER NOT NULL,
            user_name        TEXT    NULL,
            user_role        TEXT    NULL,
            from_currency    TEXT    NOT NULL,
            to_currency      TEXT    NOT NULL,
            from_amount      REAL    NOT NULL,
            to_amount        REAL    NOT NULL,
            bank             TEXT    NOT NULL,
            margin_percent   REAL    NOT NULL,
            profit_uah       REAL    NOT NULL,
            profit_eur       REAL    NULL,
            created_at_utc   TEXT    NOT NULL,
            state            TEXT    NOT NULL DEFAULT 'draft'
        )
        """;

    private const string CreateHistoryIndex =
        "CREATE INDEX IF NOT EXISTS ix_history_user_time ON conversion_history(user_id, created_at_utc DESC)";

    public void Initialize()
    {
        using var connection = factory.Open();

        Execute(connection, CreateUserSettings);
        logger.LogInformation("Schema: user_settings ready");

        Execute(connection, CreateConversionHistory);
        logger.LogInformation("Schema: conversion_history ready");

        Execute(connection, CreateHistoryIndex);

        EnsureColumn(connection, "user_settings", "user_name", "TEXT NULL");
        EnsureColumn(connection, "user_settings", "user_role", "TEXT NULL");
        EnsureColumn(connection, "user_settings", "history_enabled", "INTEGER NULL");
        EnsureColumn(connection, "user_settings", "show_best_hint", "INTEGER NULL");
        EnsureColumn(connection, "conversion_history", "user_name", "TEXT NULL");
        EnsureColumn(connection, "conversion_history", "user_role", "TEXT NULL");
        EnsureColumn(connection, "conversion_history", "profit_eur", "REAL NULL");
        EnsureColumn(connection, "conversion_history", "state", "TEXT NOT NULL DEFAULT 'draft'");

        VerifyTableExists(connection, "user_settings");
        VerifyTableExists(connection, "conversion_history");

        logger.LogInformation("SQLite database initialized at {Path}", factory.DatabasePath);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table})";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void VerifyTableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        if (command.ExecuteScalar() is not string)
            throw new InvalidOperationException($"SQLite schema init failed: table '{table}' missing after CREATE.");
    }
}
