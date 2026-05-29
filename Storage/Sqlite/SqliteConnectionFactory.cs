using System.IO;
using Microsoft.Data.Sqlite;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;

    public SqliteConnectionFactory(string databasePath)
    {
        _databasePath = databasePath;
    }

    public SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        }.ToString();

        var connection = new SqliteConnection(connectionString);

        var disposeConnection = true;

        try
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();

            disposeConnection = false;
            return connection;
        }
        finally
        {
            if (disposeConnection)
                connection.Dispose();
        }
    }
}
