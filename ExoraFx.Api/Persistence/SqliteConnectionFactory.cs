using ExoraFx.Api.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Persistence;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IOptions<StorageSettings> options)
    {
        var path = options.Value.DatabasePath;
        DatabasePath = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        Run(connection, "PRAGMA wal_checkpoint(TRUNCATE)");
        Run(connection, "PRAGMA journal_mode=DELETE");
        Run(connection, "PRAGMA synchronous=NORMAL");
        Run(connection, "PRAGMA busy_timeout=2000");

        return connection;
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
