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
        Assert.True(TableExists(connection, "icon_assets"));
        Assert.True(TableExists(connection, "items"));
        Assert.True(ColumnExists(connection, "items", "icon_asset_id"));
        Assert.True(ColumnExists(connection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(connection, "crypto_profiles", "wrapped_data_key"));
        Assert.True(ColumnExists(connection, "crypto_profiles", "kdf_hash_algorithm"));
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

    [Fact]
    public void Migrate_upgrades_version_1_schema_to_current_version()
    {
        using var database = TempSqliteDatabase.Create();
        using (var connection = database.OpenConnection())
        {
            CreateLegacyVersion1Schema(connection);
        }

        var migrator = database.CreateMigrator();

        var result = migrator.Migrate();

        using var upgradedConnection = database.OpenConnection();

        Assert.True(result.MigrationApplied);
        Assert.Equal(1, result.PreviousVersion);
        Assert.Equal(SqliteDatabaseMigrator.CurrentVersion, GetUserVersion(upgradedConnection));
        Assert.True(TableExists(upgradedConnection, "icon_assets"));
        Assert.True(ColumnExists(upgradedConnection, "items", "icon_asset_id"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "wrapped_data_key"));
        Assert.Equal("Title", GetItemTitle(upgradedConnection, "bookmark"));
    }

    [Fact]
    public void Migrate_upgrades_version_2_schema_to_current_version()
    {
        using var database = TempSqliteDatabase.Create();
        using (var connection = database.OpenConnection())
        {
            CreateLegacyVersion2Schema(connection);
        }

        var migrator = database.CreateMigrator();

        var result = migrator.Migrate();

        using var upgradedConnection = database.OpenConnection();

        Assert.True(result.MigrationApplied);
        Assert.Equal(2, result.PreviousVersion);
        Assert.Equal(SqliteDatabaseMigrator.CurrentVersion, GetUserVersion(upgradedConnection));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "wrapped_data_key"));
        Assert.Equal("Title", GetItemTitle(upgradedConnection, "bookmark"));
    }

    [Fact]
    public void Icon_assets_source_hash_is_unique()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();
        migrator.Migrate();

        using var connection = database.OpenConnection();

        InsertIconAsset(connection, "icon-1", "same-hash");

        Assert.Throws<SqliteException>(() => InsertIconAsset(connection, "icon-2", "same-hash"));
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

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($tableName);";
        command.Parameters.AddWithValue("$tableName", tableName);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), columnName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void CreateLegacyVersion1Schema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE crypto_profiles (
                id INTEGER PRIMARY KEY,
                kdf_name TEXT NOT NULL,
                kdf_iterations INTEGER NOT NULL,
                salt BLOB NOT NULL,
                encryption_algorithm TEXT NOT NULL,
                password_check_payload BLOB NULL,
                password_check_nonce BLOB NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE items (
                id TEXT PRIMARY KEY,
                parent_id TEXT NULL REFERENCES items(id) ON DELETE RESTRICT,
                item_type TEXT NOT NULL CHECK (item_type IN ('folder', 'bookmark')),
                sort_order INTEGER NOT NULL,
                title TEXT NULL,
                url TEXT NULL,
                is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1)),
                encrypted_payload BLOB NULL,
                encryption_nonce BLOB NULL,
                crypto_profile_id INTEGER NULL REFERENCES crypto_profiles(id),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                deleted_at_utc TEXT NULL,
                revision INTEGER NOT NULL DEFAULT 1,
                content_hash TEXT NULL,
                sync_state TEXT NOT NULL DEFAULT 'dirty'
                    CHECK (sync_state IN ('clean', 'dirty', 'conflict')),
                remote_etag TEXT NULL,
                last_synced_at_utc TEXT NULL,
                modified_device_id TEXT NOT NULL
            );

            CREATE INDEX idx_items_parent_order
                ON items(parent_id, deleted_at_utc, sort_order DESC, id);

            CREATE INDEX idx_items_sync_state
                ON items(sync_state);

            CREATE INDEX idx_items_updated_at
                ON items(updated_at_utc);

            INSERT INTO items (
                id,
                parent_id,
                item_type,
                sort_order,
                title,
                url,
                is_secret,
                encrypted_payload,
                encryption_nonce,
                crypto_profile_id,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                content_hash,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id)
            VALUES (
                'bookmark',
                NULL,
                'bookmark',
                1000,
                'Title',
                'https://example.com',
                0,
                NULL,
                NULL,
                NULL,
                '2026-01-01T00:00:00.0000000Z',
                '2026-01-01T00:00:00.0000000Z',
                NULL,
                1,
                NULL,
                'dirty',
                NULL,
                NULL,
                'test-device');

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateLegacyVersion2Schema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE crypto_profiles (
                id INTEGER PRIMARY KEY,
                kdf_name TEXT NOT NULL,
                kdf_iterations INTEGER NOT NULL,
                salt BLOB NOT NULL,
                encryption_algorithm TEXT NOT NULL,
                password_check_payload BLOB NULL,
                password_check_nonce BLOB NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE icon_assets (
                id TEXT PRIMARY KEY,
                source_hash_algorithm TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                source_size_bytes INTEGER NOT NULL,
                processed_mime_type TEXT NOT NULL,
                processed_width INTEGER NOT NULL,
                processed_height INTEGER NOT NULL,
                processed_bytes BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_hash_algorithm, source_hash)
            );

            CREATE TABLE items (
                id TEXT PRIMARY KEY,
                parent_id TEXT NULL REFERENCES items(id) ON DELETE RESTRICT,
                item_type TEXT NOT NULL CHECK (item_type IN ('folder', 'bookmark')),
                sort_order INTEGER NOT NULL,
                title TEXT NULL,
                url TEXT NULL,
                icon_asset_id TEXT NULL REFERENCES icon_assets(id),
                is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1)),
                encrypted_payload BLOB NULL,
                encryption_nonce BLOB NULL,
                crypto_profile_id INTEGER NULL REFERENCES crypto_profiles(id),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                deleted_at_utc TEXT NULL,
                revision INTEGER NOT NULL DEFAULT 1,
                content_hash TEXT NULL,
                sync_state TEXT NOT NULL DEFAULT 'dirty'
                    CHECK (sync_state IN ('clean', 'dirty', 'conflict')),
                remote_etag TEXT NULL,
                last_synced_at_utc TEXT NULL,
                modified_device_id TEXT NOT NULL
            );

            CREATE INDEX idx_items_parent_order
                ON items(parent_id, deleted_at_utc, sort_order DESC, id);

            CREATE INDEX idx_items_sync_state
                ON items(sync_state);

            CREATE INDEX idx_items_updated_at
                ON items(updated_at_utc);

            INSERT INTO items (
                id,
                parent_id,
                item_type,
                sort_order,
                title,
                url,
                icon_asset_id,
                is_secret,
                encrypted_payload,
                encryption_nonce,
                crypto_profile_id,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                content_hash,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id)
            VALUES (
                'bookmark',
                NULL,
                'bookmark',
                1000,
                'Title',
                'https://example.com',
                NULL,
                0,
                NULL,
                NULL,
                NULL,
                '2026-01-01T00:00:00.0000000Z',
                '2026-01-01T00:00:00.0000000Z',
                NULL,
                1,
                NULL,
                'dirty',
                NULL,
                NULL,
                'test-device');

            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertIconAsset(SqliteConnection connection, string id, string sourceHash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO icon_assets (
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                processed_bytes,
                created_at_utc)
            VALUES (
                $id,
                'sha256',
                $sourceHash,
                12,
                'image/png',
                64,
                64,
                X'010203',
                '2026-01-01T00:00:00.0000000Z');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sourceHash", sourceHash);
        command.ExecuteNonQuery();
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

    private static string GetItemTitle(SqliteConnection connection, string itemId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title FROM items WHERE id = $itemId;";
        command.Parameters.AddWithValue("$itemId", itemId);

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
