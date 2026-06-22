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
        Assert.True(TableExists(connection, "secret_icon_assets"));
        Assert.True(TableExists(connection, "items"));
        Assert.True(TableExists(connection, "secret_reset_events"));
        Assert.True(TableExists(connection, "sync_pending_asset_refs"));
        Assert.True(TableExists(connection, "sync_deferred_secret_items"));
        Assert.True(TableExists(connection, "sync_quarantined_remote_objects"));
        Assert.True(ColumnExists(connection, "items", "icon_asset_id"));
        Assert.True(ColumnExists(connection, "items", "secret_icon_asset_id"));
        Assert.True(ColumnExists(connection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(connection, "items", "secret_generation_id"));
        Assert.True(ColumnExists(connection, "crypto_profiles", "wrapped_data_key"));
        Assert.True(ColumnExists(connection, "crypto_profiles", "kdf_hash_algorithm"));
        Assert.True(ColumnExists(connection, "crypto_profiles", "secret_generation_id"));
        AssertSyncColumnsExist(connection, "icon_assets");
        AssertSyncColumnsExist(connection, "secret_icon_assets");
        AssertSyncColumnsExist(connection, "crypto_profiles");
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
        Assert.True(TableExists(upgradedConnection, "secret_icon_assets"));
        Assert.True(ColumnExists(upgradedConnection, "items", "icon_asset_id"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_icon_asset_id"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_generation_id"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "wrapped_data_key"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "secret_generation_id"));
        Assert.True(TableExists(upgradedConnection, "secret_reset_events"));
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
        Assert.True(TableExists(upgradedConnection, "secret_icon_assets"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_icon_asset_id"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_payload_format_version"));
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_generation_id"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "wrapped_data_key"));
        Assert.True(ColumnExists(upgradedConnection, "crypto_profiles", "secret_generation_id"));
        Assert.True(TableExists(upgradedConnection, "secret_reset_events"));
        Assert.Equal("Title", GetItemTitle(upgradedConnection, "bookmark"));
    }

    [Fact]
    public void Migrate_upgrades_version_5_schema_to_current_version_with_sync_metadata()
    {
        using var database = TempSqliteDatabase.Create();
        using (var connection = database.OpenConnection())
        {
            CreateLegacyVersion5Schema(connection);
        }

        var migrator = database.CreateMigrator();

        var result = migrator.Migrate();

        using var upgradedConnection = database.OpenConnection();

        Assert.True(result.MigrationApplied);
        Assert.Equal(5, result.PreviousVersion);
        Assert.Equal(SqliteDatabaseMigrator.CurrentVersion, GetUserVersion(upgradedConnection));
        AssertSyncColumnsExist(upgradedConnection, "icon_assets");
        AssertSyncColumnsExist(upgradedConnection, "secret_icon_assets");
        AssertSyncColumnsExist(upgradedConnection, "crypto_profiles");
        Assert.True(ColumnExists(upgradedConnection, "items", "secret_generation_id"));
        Assert.True(TableExists(upgradedConnection, "sync_pending_asset_refs"));
        Assert.True(TableExists(upgradedConnection, "sync_deferred_secret_items"));
        Assert.True(TableExists(upgradedConnection, "sync_quarantined_remote_objects"));
        Assert.Equal("dirty", GetStringValue(upgradedConnection, "icon_assets", "sync_state", "icon"));
        Assert.Equal("id-2", GetStringValue(upgradedConnection, "icon_assets", "modified_device_id", "icon"));
        Assert.Equal("dirty", GetStringValue(upgradedConnection, "secret_icon_assets", "sync_state", "secret-icon"));
        Assert.Equal("id-2", GetStringValue(upgradedConnection, "secret_icon_assets", "modified_device_id", "secret-icon"));
        Assert.Equal("dirty", GetStringValue(upgradedConnection, "crypto_profiles", "sync_state", "1"));
        Assert.Equal("id-2", GetStringValue(upgradedConnection, "crypto_profiles", "modified_device_id", "1"));
        Assert.Equal(0, CountRows(upgradedConnection, "sync_pending_asset_refs"));
        Assert.Equal(0, CountRows(upgradedConnection, "sync_deferred_secret_items"));
        Assert.Equal(0, CountRows(upgradedConnection, "sync_quarantined_remote_objects"));
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

    [Fact]
    public void Secret_icon_assets_source_hash_is_unique()
    {
        using var database = TempSqliteDatabase.Create();
        var migrator = database.CreateMigrator();
        migrator.Migrate();

        using var connection = database.OpenConnection();

        InsertSecretIconAsset(connection, "secret-icon-1", "same-hash");

        Assert.Throws<SqliteException>(() => InsertSecretIconAsset(connection, "secret-icon-2", "same-hash"));
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

    private static void AssertSyncColumnsExist(SqliteConnection connection, string tableName)
    {
        Assert.True(ColumnExists(connection, tableName, "sync_state"));
        Assert.True(ColumnExists(connection, tableName, "remote_etag"));
        Assert.True(ColumnExists(connection, tableName, "last_synced_at_utc"));
        Assert.True(ColumnExists(connection, tableName, "content_hash"));
        Assert.True(ColumnExists(connection, tableName, "modified_device_id"));
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

    private static void CreateLegacyVersion5Schema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE crypto_profiles (
                id INTEGER PRIMARY KEY,
                profile_version INTEGER NOT NULL,
                kdf_name TEXT NOT NULL,
                kdf_hash_algorithm TEXT NOT NULL,
                kdf_iterations INTEGER NOT NULL,
                kdf_salt BLOB NOT NULL,
                kek_length_bytes INTEGER NOT NULL,
                data_key_algorithm TEXT NOT NULL,
                wrapped_data_key BLOB NOT NULL,
                wrapped_data_key_nonce BLOB NOT NULL,
                encryption_algorithm TEXT NOT NULL,
                payload_format TEXT NOT NULL,
                password_check_payload BLOB NOT NULL,
                password_check_nonce BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                secret_generation_id TEXT NOT NULL
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

            CREATE TABLE secret_icon_assets (
                id TEXT PRIMARY KEY,
                source_hash_algorithm TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                source_size_bytes INTEGER NOT NULL,
                processed_mime_type TEXT NOT NULL,
                processed_width INTEGER NOT NULL,
                processed_height INTEGER NOT NULL,
                encrypted_processed_bytes BLOB NOT NULL,
                encryption_nonce BLOB NOT NULL,
                payload_format_version INTEGER NOT NULL,
                secret_generation_id TEXT NOT NULL,
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
                secret_icon_asset_id TEXT NULL REFERENCES secret_icon_assets(id),
                is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1)),
                encrypted_payload BLOB NULL,
                encryption_nonce BLOB NULL,
                crypto_profile_id INTEGER NULL REFERENCES crypto_profiles(id),
                secret_payload_format_version INTEGER NULL,
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

            CREATE TABLE secret_reset_events (
                id TEXT PRIMARY KEY,
                secret_generation_id TEXT NOT NULL UNIQUE,
                reset_at_utc TEXT NOT NULL,
                reset_device_id TEXT NOT NULL,
                sync_state TEXT NOT NULL DEFAULT 'dirty'
                    CHECK (sync_state IN ('clean', 'dirty', 'conflict')),
                remote_etag TEXT NULL,
                last_synced_at_utc TEXT NULL
            );

            INSERT INTO crypto_profiles (
                id,
                profile_version,
                kdf_name,
                kdf_hash_algorithm,
                kdf_iterations,
                kdf_salt,
                kek_length_bytes,
                data_key_algorithm,
                wrapped_data_key,
                wrapped_data_key_nonce,
                encryption_algorithm,
                payload_format,
                password_check_payload,
                password_check_nonce,
                created_at_utc,
                updated_at_utc,
                secret_generation_id)
            VALUES (
                1,
                1,
                'PBKDF2',
                'SHA256',
                1,
                X'010203',
                32,
                'AES-GCM',
                X'040506',
                X'070809',
                'AES-GCM',
                'v1',
                X'0A0B0C',
                X'0D0E0F',
                '2026-01-01T00:00:00.0000000Z',
                '2026-01-01T00:00:00.0000000Z',
                'generation');

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
                'icon',
                'sha256',
                'icon-hash',
                12,
                'image/png',
                64,
                64,
                X'010203',
                '2026-01-01T00:00:00.0000000Z');

            INSERT INTO secret_icon_assets (
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                encrypted_processed_bytes,
                encryption_nonce,
                payload_format_version,
                secret_generation_id,
                created_at_utc)
            VALUES (
                'secret-icon',
                'sha256',
                'secret-icon-hash',
                12,
                'image/png',
                64,
                64,
                X'010203',
                X'040506',
                1,
                'generation',
                '2026-01-01T00:00:00.0000000Z');

            PRAGMA user_version = 5;
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

    private static void InsertSecretIconAsset(SqliteConnection connection, string id, string sourceHash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO secret_icon_assets (
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                encrypted_processed_bytes,
                encryption_nonce,
                payload_format_version,
                secret_generation_id,
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
                X'040506',
                1,
                'generation',
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

    private static string GetStringValue(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {columnName} FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
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
