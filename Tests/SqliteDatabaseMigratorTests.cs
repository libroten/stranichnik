using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Stranichnik.Storage.Sqlite;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteDatabaseMigratorTests
{
    [Fact]
    public void Migrate_creates_schema_from_empty_file()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();

        migrator.Migrate();

        using var connection = database.OpenConnection();

        Assert.True(TableExists(connection, "app_meta"));
        Assert.True(TableExists(connection, "crypto_profiles"));
        Assert.True(TableExists(connection, "items"));
    }

    [Fact]
    public void Migrate_sets_user_version_to_current_version()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();

        migrator.Migrate();

        using var connection = database.OpenConnection();

        Assert.Equal(SqliteDatabaseMigrator.CurrentVersion, GetUserVersion(connection));
    }

    [Fact]
    public void Migrate_inserts_database_and_device_ids()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();

        migrator.Migrate();

        using var connection = database.OpenConnection();

        Assert.Equal("id-1", GetMetaValue(connection, "database_id"));
        Assert.Equal("id-2", GetMetaValue(connection, "device_id"));
    }

    [Fact]
    public void Migrate_can_run_twice_without_failing()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();

        var first = migrator.Migrate();
        var second = migrator.Migrate();

        using var connection = database.OpenConnection();

        Assert.True(first.MigrationApplied);
        Assert.False(second.MigrationApplied);
        Assert.Equal(SqliteDatabaseMigrator.CurrentVersion, GetUserVersion(connection));
        Assert.Equal("id-1", GetMetaValue(connection, "database_id"));
        Assert.Equal("id-2", GetMetaValue(connection, "device_id"));
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
                AND name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        return command.ExecuteScalar() is not null;
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetMetaValue(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;
        private int _nextId = 1;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            ConnectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
        }

        private SqliteConnectionFactory ConnectionFactory { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public SqliteDatabaseMigrator CreateMigrator()
        {
            return new SqliteDatabaseMigrator(
                ConnectionFactory,
                idFactory: () => $"id-{_nextId++}");
        }

        public SqliteConnection OpenConnection()
        {
            return ConnectionFactory.OpenConnection();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                    Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
