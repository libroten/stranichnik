using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Stranichnik.Security;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSecretResetStore : ISecretResetStore
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _resetDeviceId;

    public SqliteSecretResetStore(
        SqliteConnectionFactory connectionFactory,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null,
        string? resetDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connectionFactory = connectionFactory;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _resetDeviceId = string.IsNullOrWhiteSpace(resetDeviceId)
            ? "local"
            : resetDeviceId;
    }

    public SecretResetStoreResult ResetMasterPasswordAndPurgeSecrets(string secretGenerationId)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID must not be empty.", nameof(secretGenerationId));

        try
        {
            using var connection = _connectionFactory.OpenConnection();
            using var transaction = connection.BeginTransaction();

            EnsureActiveProfileGenerationExists(connection, transaction, secretGenerationId);
            InsertResetEvent(connection, transaction, secretGenerationId);
            var purgedCount = CountSecretBookmarks(connection, transaction);
            var folderIdsToPurge = SelectSecretOnlyFolderIds(connection, transaction);
            DeleteSecretBookmarks(connection, transaction);
            DeleteFolders(connection, transaction, folderIdsToPurge);
            DeleteActiveProfile(connection, transaction, secretGenerationId);

            transaction.Commit();
            return new SecretResetStoreResult(purgedCount);
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException("Secret reset storage operation failed.", exception);
        }
    }

    public IReadOnlyList<SecretResetEventRecord> LoadResetEvents()
    {
        using var connection = _connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                secret_generation_id,
                reset_at_utc,
                reset_device_id,
                sync_state,
                remote_etag,
                last_synced_at_utc
            FROM secret_reset_events
            ORDER BY reset_at_utc ASC, id ASC;
            """;

        var events = new List<SecretResetEventRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            events.Add(ReadResetEvent(reader));

        return events;
    }

    private static void EnsureActiveProfileGenerationExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM crypto_profiles
            WHERE id = $id
                AND secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$id", SecretCryptoProfileIds.ActiveProfileId);
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);

        if (command.ExecuteScalar() is null)
            throw new InvalidOperationException("Secret crypto profile was not found.");
    }

    private void InsertResetEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO secret_reset_events (
                id,
                secret_generation_id,
                reset_at_utc,
                reset_device_id,
                sync_state,
                remote_etag,
                last_synced_at_utc)
            VALUES (
                $id,
                $secretGenerationId,
                $resetAtUtc,
                $resetDeviceId,
                $syncState,
                NULL,
                NULL);
            """;
        command.Parameters.AddWithValue("$id", _idFactory());
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.Parameters.AddWithValue("$resetAtUtc", SqliteBookmarkItemMapper.FormatDateTime(_clock()));
        command.Parameters.AddWithValue("$resetDeviceId", _resetDeviceId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(BookmarkSyncState.Dirty));
        command.ExecuteNonQuery();
    }

    private static int CountSecretBookmarks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM items
            WHERE item_type = 'bookmark'
                AND is_secret = 1;
            """;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> SelectSecretOnlyFolderIds(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE
                visible_folders(id) AS (
                    SELECT folder.id
                    FROM items folder
                    WHERE folder.item_type = 'folder'
                        AND folder.deleted_at_utc IS NULL
                        AND (
                            NOT EXISTS (
                                SELECT 1
                                FROM items child
                                WHERE child.parent_id = folder.id
                                    AND child.deleted_at_utc IS NULL
                            )
                            OR EXISTS (
                                SELECT 1
                                FROM items child
                                WHERE child.parent_id = folder.id
                                    AND child.deleted_at_utc IS NULL
                                    AND child.item_type = 'bookmark'
                                    AND child.is_secret = 0
                            )
                        )

                    UNION

                    SELECT parent.id
                    FROM items parent
                    INNER JOIN items child
                        ON child.parent_id = parent.id
                        AND child.deleted_at_utc IS NULL
                        AND child.item_type = 'folder'
                    INNER JOIN visible_folders visible_child
                        ON visible_child.id = child.id
                    WHERE parent.item_type = 'folder'
                        AND parent.deleted_at_utc IS NULL
                ),
                folder_depths(id, depth) AS (
                    SELECT id, 0
                    FROM items
                    WHERE item_type = 'folder'
                        AND parent_id IS NULL

                    UNION ALL

                    SELECT child.id, parent.depth + 1
                    FROM items child
                    INNER JOIN folder_depths parent
                        ON parent.id = child.parent_id
                    WHERE child.item_type = 'folder'
                ),
                secret_ancestor_folders(id) AS (
                    SELECT parent_id
                    FROM items
                    WHERE item_type = 'bookmark'
                        AND is_secret = 1
                        AND parent_id IS NOT NULL

                    UNION

                    SELECT parent.parent_id
                    FROM items parent
                    INNER JOIN secret_ancestor_folders child_folder
                        ON parent.id = child_folder.id
                    WHERE parent.parent_id IS NOT NULL
                ),
                folders_to_purge(id) AS (
                    SELECT id
                    FROM secret_ancestor_folders
                    WHERE id NOT IN (SELECT id FROM visible_folders)
                )
            SELECT folder.id
            FROM folders_to_purge folder
            INNER JOIN folder_depths depth
                ON depth.id = folder.id
            ORDER BY depth.depth DESC;
            """;
        using var reader = command.ExecuteReader();
        var folderIds = new List<string>();

        while (reader.Read())
            folderIds.Add(reader.GetString(0));

        return folderIds;
    }

    private static void DeleteSecretBookmarks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM items
            WHERE item_type = 'bookmark'
                AND is_secret = 1;
            """;
        command.ExecuteNonQuery();
    }

    private static void DeleteFolders(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<string> folderIds)
    {
        foreach (var folderId in folderIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM items
                WHERE id = $id
                    AND item_type = 'folder'
                    AND NOT EXISTS (
                        SELECT 1
                        FROM items child
                        WHERE child.parent_id = $id
                    );
                """;
            command.Parameters.AddWithValue("$id", folderId);
            command.ExecuteNonQuery();
        }
    }

    private static void DeleteActiveProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM crypto_profiles
            WHERE id = $id
                AND secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$id", SecretCryptoProfileIds.ActiveProfileId);
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret crypto profile was not found.");
    }

    private static SecretResetEventRecord ReadResetEvent(SqliteDataReader reader)
    {
        return new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("secret_generation_id")),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("reset_at_utc"))),
            reader.GetString(reader.GetOrdinal("reset_device_id")),
            SqliteBookmarkItemMapper.ToBookmarkSyncState(reader.GetString(reader.GetOrdinal("sync_state"))),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "remote_etag"),
            SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "last_synced_at_utc"));
    }
}
