using System;
using Microsoft.Data.Sqlite;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteDatabaseMigrator
{
    public const int CurrentVersion = 7;
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

        if (currentVersion == 4)
        {
            ApplyVersion5(connection);
            migrationApplied = true;
            currentVersion = 5;
        }

        if (currentVersion == 5)
        {
            EnsureMetadata(connection, "database_id", _idFactory());
            EnsureMetadata(connection, "device_id", _idFactory());
            ApplyVersion6(connection, GetMetadataValue(connection, "device_id"));
            migrationApplied = true;
            currentVersion = 6;
        }

        if (currentVersion == 6)
        {
            ApplyVersion7(connection);
            migrationApplied = true;
            currentVersion = 7;
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

        CreateSecretIconAssetsTable(connection, transaction);
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
                secret_icon_asset_id,
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
                NULL,
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

    private static void ApplyVersion5(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = OFF;");

        using var transaction = connection.BeginTransaction();

        CreateSecretIconAssetsTable(connection, transaction);

        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE items RENAME TO items_v5;");

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
                secret_icon_asset_id,
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
                NULL,
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
                modified_device_id
            FROM items_v5;
            """);

        ExecuteNonQuery(connection, transaction, "DROP TABLE items_v5;");
        CreateItemIndexes(connection, transaction);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 5;");

        transaction.Commit();

        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
    }

    private static void ApplyVersion6(SqliteConnection connection, string deviceId)
    {
        using var transaction = connection.BeginTransaction();

        AddSyncMetadataColumns(connection, transaction, "icon_assets", deviceId);
        AddSyncMetadataColumns(connection, transaction, "secret_icon_assets", deviceId);
        AddSyncMetadataColumns(connection, transaction, "crypto_profiles", deviceId);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE sync_pending_asset_refs (
                id TEXT PRIMARY KEY,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                asset_kind TEXT NOT NULL CHECK (asset_kind IN ('regular-icon', 'secret-icon')),
                remote_asset_id TEXT NOT NULL,
                source_hash_algorithm TEXT NULL,
                source_hash TEXT NULL,
                created_at_utc TEXT NOT NULL,
                last_attempt_at_utc TEXT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error_code TEXT NULL,
                UNIQUE (item_id, asset_kind)
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE sync_deferred_secret_items (
                remote_item_id TEXT PRIMARY KEY,
                secret_generation_id TEXT NOT NULL,
                remote_etag TEXT NULL,
                content_hash TEXT NOT NULL,
                canonical_json BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_attempt_at_utc TEXT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error_code TEXT NULL
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE sync_quarantined_remote_objects (
                id TEXT PRIMARY KEY,
                object_kind TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                remote_etag TEXT NULL,
                content_hash TEXT NULL,
                reason_code TEXT NOT NULL,
                first_seen_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                seen_count INTEGER NOT NULL DEFAULT 1
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_sync_pending_asset_refs_remote_asset
                ON sync_pending_asset_refs(asset_kind, remote_asset_id);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_sync_deferred_secret_items_generation
                ON sync_deferred_secret_items(secret_generation_id);
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_sync_quarantined_remote_objects_kind
                ON sync_quarantined_remote_objects(object_kind, reason_code);
            """);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 6;");

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

    private static string GetMetadataValue(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("SQLite database metadata is missing.");
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

    private static void AddSyncMetadataColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string deviceId)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            $"""
            ALTER TABLE {tableName}
                ADD COLUMN sync_state TEXT NOT NULL DEFAULT 'dirty'
                    CHECK (sync_state IN ('clean', 'dirty', 'conflict'));
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN remote_etag TEXT NULL;");

        ExecuteNonQuery(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN last_synced_at_utc TEXT NULL;");

        ExecuteNonQuery(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN content_hash TEXT NULL;");

        ExecuteNonQuery(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN modified_device_id TEXT NOT NULL DEFAULT '{EscapeSqlLiteral(deviceId)}';");

        ExecuteNonQuery(
            connection,
            transaction,
            $"""
            CREATE INDEX idx_{tableName}_sync_state
                ON {tableName}(sync_state);
            """);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
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
                        AND secret_payload_format_version IS NULL
                        AND secret_icon_asset_id IS NULL)

                    OR

                    (item_type = 'bookmark'
                        AND is_secret = 0
                        AND title IS NOT NULL
                        AND url IS NOT NULL
                        AND encrypted_payload IS NULL
                        AND encryption_nonce IS NULL
                        AND crypto_profile_id IS NULL
                        AND secret_payload_format_version IS NULL
                        AND secret_icon_asset_id IS NULL)

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

    private static void CreateSecretIconAssetsTable(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS secret_icon_assets (
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
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS idx_secret_icon_assets_generation
                ON secret_icon_assets(secret_generation_id);
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

    private static void ApplyVersion7(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            transaction,
            """
            ALTER TABLE items
                ADD COLUMN secret_generation_id TEXT NULL;
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE items
            SET secret_generation_id = (
                SELECT crypto_profiles.secret_generation_id
                FROM crypto_profiles
                WHERE crypto_profiles.id = items.crypto_profile_id
            )
            WHERE is_secret = 1
                AND crypto_profile_id IS NOT NULL;
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE INDEX idx_items_secret_generation
                ON items(secret_generation_id);
            """);

        ExecuteNonQuery(connection, transaction, "PRAGMA user_version = 7;");

        transaction.Commit();
    }
}

public sealed record SqliteMigrationResult(
    int PreviousVersion,
    int CurrentVersion,
    bool MigrationApplied);
