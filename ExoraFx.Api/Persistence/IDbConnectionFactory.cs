using Microsoft.Data.Sqlite;

namespace ExoraFx.Api.Persistence;

public interface IDbConnectionFactory
{
    SqliteConnection Open();

    string DatabasePath { get; }
}
