using System;
using Microsoft.Data.Sqlite;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteDatabaseMigrator
{
    public const int CurrentVersion = 4;
    private const string LegacySecretGenerationId = "legacy-generation";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<string> _idFactory;

    public SqliteDatabaseMigrator(
        SqliteConnectionFactory connectionFactory,
        Func<string>? idFactory = null)
    {
        _connectionFactory = connectionFactory;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public SqliteMigrationResult Migrate()
    {
        using var connection = _connectionFactory.OpenConnection();
        var previousVersion = GetUserVersion(connection);
        var migrationApplied = false;

        if (previousVersion == 0)
        {
            ApplyVersion1(connection);
            migrationApplied = true;
        }

        var currentVersion = GetUserVersion(connection);

        if (currentVersion == 1)
        {
            ApplyVersion2(connection);
            migrationApplied = true;
            currentVersion = 2;
        }

        if (currentVersion == 2)
        {
            ApplyVersion3(connection);
            migrationApplied = true;
            currentVersion = 3;
        }

        if (currentVersion == 3)
        {
            ApplyVersion4(connection);
            migrationApplied = true;
            currentVersion = 4;
        }

        if (currentVersion > CurrentVersion)
        {
            throw new InvalidOperationException("SQLite database schema is newer than this application supports.");
        }

        EnsureMetadata(connection, "database_id", _idFactory());
        EnsureMetadata(connection, "device_id", _idFactory());

        return new SqliteMigrationResult(previousVersion, CurrentVersion, migrationApplied);
    }

    public string GetMetadataValue(string key)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("SQLite database metadata is missing.");
    }

    private static void ApplyVersion1(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
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
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
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
                modified_device_id TEXT NOT NULL,

                CHECK (parent_id IS NULL OR parent_id <> id),

                CHECK (
                    (item_type = 'folder'
                        AND is_secret = 0
                        AND title IS NOT NULL
                        AND url IS NULL
                        AND encrypted_payload IS NULL
                        AND encryption_nonce IS NULL
                        AND crypto_profile_id IS NULL)

                    OR

                    (item_type = 'bookmark'
                        AND is_secret = 0
                        AND title IS NOT NULL
                        AND url IS NOT NULL
                        AND encrypted_payload IS NULL
                        AND encryption_nonce IS NULL
                        AND crypto_profile_id IS NULL)

                    OR

                    (item_type = 'bookmark'
                        AND is_secret = 1
                        AND title IS NULL
                        AND url IS NULL
                        AND encrypted_payload IS NOT NULL
                        AND encryption_nonce IS NOT NULL
                        AND crypto_profile_id IS NOT NULL)
                )
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_parent_order
                ON items(parent_id, deleted_at_utc, sort_order DESC, id);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_sync_state
                ON items(sync_state);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_updated_at
                ON items(updated_at_utc);
            """);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 1;");

        transaction.Commit();
    }

    private static void ApplyVersion2(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            """
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
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            ALTER TABLE items
                ADD COLUMN icon_asset_id TEXT NULL REFERENCES icon_assets(id);
            """);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 2;");

        transaction.Commit();
    }

    private static void ApplyVersion3(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = OFF;");

        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE crypto_profiles RENAME TO crypto_profiles_v2;");

        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE items RENAME TO items_v2;");

        CreateCryptoProfilesTable(connection, transaction);
        CreateItemsTable(connection, transaction);

        ExecuteNonQuery(
            connection,
            transaction,
            """
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
                secret_payload_format_version,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                content_hash,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id)
            SELECT
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
                CASE
                    WHEN is_secret = 1 THEN 1
                    ELSE NULL
                END,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                content_hash,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id
            FROM items_v2;
            """);

        ExecuteNonQuery(connection, transaction, "DROP TABLE items_v2;");
        ExecuteNonQuery(connection, transaction, "DROP TABLE crypto_profiles_v2;");

        CreateItemIndexes(connection, transaction);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 3;");

        transaction.Commit();

        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
    }

    private static void ApplyVersion4(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            $"""
            ALTER TABLE crypto_profiles
                ADD COLUMN secret_generation_id TEXT NOT NULL DEFAULT '{LegacySecretGenerationId}';
            """);

        CreateSecretResetEventsTable(connection, transaction);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 4;");

        transaction.Commit();
    }

    private static void EnsureMetadata(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO app_meta (key, value)
            VALUES ($key, $value);
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void CreateCryptoProfilesTable(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
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
                updated_at_utc TEXT NOT NULL
            );
            """);
    }

    private static void CreateItemsTable(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
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
                modified_device_id TEXT NOT NULL,

                CHECK (parent_id IS NULL OR parent_id <> id),

                CHECK (
                    (item_type = 'folder'
                        AND is_secret = 0
                        AND title IS NOT NULL
                        AND url IS NULL
                        AND encrypted_payload IS NULL
                        AND encryption_nonce IS NULL
                        AND crypto_profile_id IS NULL
                        AND secret_payload_format_version IS NULL)

                    OR

                    (item_type = 'bookmark'
                        AND is_secret = 0
                        AND title IS NOT NULL
                        AND url IS NOT NULL
                        AND encrypted_payload IS NULL
                        AND encryption_nonce IS NULL
                        AND crypto_profile_id IS NULL
                        AND secret_payload_format_version IS NULL)

                    OR

                    (item_type = 'bookmark'
                        AND is_secret = 1
                        AND title IS NULL
                        AND url IS NULL
                        AND icon_asset_id IS NULL
                        AND encrypted_payload IS NOT NULL
                        AND encryption_nonce IS NOT NULL
                        AND crypto_profile_id IS NOT NULL
                        AND secret_payload_format_version IS NOT NULL)
                )
            );
            """);
    }

    private static void CreateItemIndexes(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_parent_order
                ON items(parent_id, deleted_at_utc, sort_order DESC, id);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_sync_state
                ON items(sync_state);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_updated_at
                ON items(updated_at_utc);
            """);
    }

    private static void CreateSecretResetEventsTable(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
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
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_secret_reset_events_sync_state
                ON secret_reset_events(sync_state);
            """);
    }
}

public sealed record SqliteMigrationResult(
    int PreviousVersion,
    int CurrentVersion,
    bool MigrationApplied);
