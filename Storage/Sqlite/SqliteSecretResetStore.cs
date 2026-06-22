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
            var purgedCount = CountSecretBookmarksForGeneration(connection, transaction, secretGenerationId);
            var folderIdsToPurge = SelectSecretOnlyFolderIdsForGeneration(connection, transaction, secretGenerationId);
            DeleteSecretBookmarksForGeneration(connection, transaction, secretGenerationId);
            DeleteFolders(connection, transaction, folderIdsToPurge);
            DeleteSecretIconAssets(connection, transaction, secretGenerationId);
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
        return LoadResetEvents(connection, transaction: null);
    }

    internal static IReadOnlyList<SecretResetEventRecord> LoadResetEvents(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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

    internal void ApplyRemoteResetEventAndPurgeSecrets(
        SecretResetEventRecord resetEvent)
    {
        ArgumentNullException.ThrowIfNull(resetEvent);

        try
        {
            using var connection = _connectionFactory.OpenConnection();
            using var transaction = connection.BeginTransaction();

            UpsertRemoteResetEvent(connection, transaction, resetEvent);
            var folderIdsToPurge = SelectSecretOnlyFolderIdsForGeneration(
                connection,
                transaction,
                resetEvent.SecretGenerationId);
            DeleteSecretBookmarksForGeneration(connection, transaction, resetEvent.SecretGenerationId);
            DeleteFolders(connection, transaction, folderIdsToPurge);
            DeleteSecretIconAssets(connection, transaction, resetEvent.SecretGenerationId);
            DeleteActiveProfileIfGenerationMatches(connection, transaction, resetEvent.SecretGenerationId);

            transaction.Commit();
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException("Remote secret reset storage operation failed.", exception);
        }
    }

    internal static void ApplyRemoteResetEventAndPurgeSecrets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SecretResetEventRecord resetEvent)
    {
        ArgumentNullException.ThrowIfNull(resetEvent);

        try
        {
            UpsertRemoteResetEvent(connection, transaction, resetEvent);
            var folderIdsToPurge = SelectSecretOnlyFolderIdsForGeneration(
                connection,
                transaction,
                resetEvent.SecretGenerationId);
            DeleteSecretBookmarksForGeneration(connection, transaction, resetEvent.SecretGenerationId);
            DeleteFolders(connection, transaction, folderIdsToPurge);
            DeleteSecretIconAssets(connection, transaction, resetEvent.SecretGenerationId);
            DeleteActiveProfileIfGenerationMatches(connection, transaction, resetEvent.SecretGenerationId);
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException("Remote secret reset storage operation failed.", exception);
        }
    }

    internal void MarkSyncMetadata(
        string secretGenerationId,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var connection = _connectionFactory.OpenConnection();
        MarkSyncMetadata(connection, transaction: null, secretGenerationId, syncState, remoteEtag, lastSyncedAtUtc);
    }

    internal static void MarkSyncMetadata(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string secretGenerationId,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE secret_reset_events
            SET
                sync_state = $syncState,
                remote_etag = $remoteEtag,
                last_synced_at_utc = $lastSyncedAtUtc
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(syncState));
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(remoteEtag));
        command.Parameters.AddWithValue("$lastSyncedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(lastSyncedAtUtc));

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret reset event was not found.");
    }

    internal void MarkSyncState(
        string secretGenerationId,
        BookmarkSyncState syncState)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var connection = _connectionFactory.OpenConnection();
        MarkSyncState(connection, transaction: null, secretGenerationId, syncState);
    }

    internal static void MarkSyncState(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string secretGenerationId,
        BookmarkSyncState syncState)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE secret_reset_events
            SET sync_state = $syncState
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(syncState));

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret reset event was not found.");
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

    private static int CountSecretBookmarksForGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM items
            WHERE item_type = 'bookmark'
                AND is_secret = 1
                AND secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> SelectSecretOnlyFolderIdsForGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE
                target_secret_bookmarks(id, parent_id) AS (
                    SELECT item.id, item.parent_id
                    FROM items item
                    WHERE item.item_type = 'bookmark'
                        AND item.is_secret = 1
                        AND item.secret_generation_id = $secretGenerationId
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
                folders_to_purge(id) AS (
                    SELECT parent_id
                    FROM target_secret_bookmarks
                    WHERE parent_id IS NOT NULL

                    UNION

                    SELECT parent.parent_id
                    FROM items parent
                    INNER JOIN folders_to_purge child_folder
                        ON parent.id = child_folder.id
                    WHERE parent.parent_id IS NOT NULL
                )
            SELECT folder.id
            FROM folders_to_purge folder
            INNER JOIN folder_depths depth
                ON depth.id = folder.id
            ORDER BY depth.depth DESC;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);

        using var reader = command.ExecuteReader();
        var folderIds = new List<string>();

        while (reader.Read())
            folderIds.Add(reader.GetString(0));

        return folderIds;
    }

    private static void DeleteSecretBookmarksForGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM items
            WHERE item_type = 'bookmark'
                AND is_secret = 1
                AND secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
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

    private static void DeleteSecretIconAssets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM secret_icon_assets
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.ExecuteNonQuery();
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

    private static void DeleteActiveProfileIfGenerationMatches(
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
        command.ExecuteNonQuery();
    }

    private static void UpsertRemoteResetEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SecretResetEventRecord resetEvent)
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
                $remoteEtag,
                $lastSyncedAtUtc)
            ON CONFLICT(secret_generation_id) DO UPDATE SET
                id = excluded.id,
                reset_at_utc = excluded.reset_at_utc,
                reset_device_id = excluded.reset_device_id,
                sync_state = excluded.sync_state,
                remote_etag = excluded.remote_etag,
                last_synced_at_utc = excluded.last_synced_at_utc;
            """;
        command.Parameters.AddWithValue("$id", resetEvent.Id);
        command.Parameters.AddWithValue("$secretGenerationId", resetEvent.SecretGenerationId);
        command.Parameters.AddWithValue("$resetAtUtc", SqliteBookmarkItemMapper.FormatDateTime(resetEvent.ResetAtUtc));
        command.Parameters.AddWithValue("$resetDeviceId", resetEvent.ResetDeviceId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(resetEvent.SyncState));
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(resetEvent.RemoteEtag));
        command.Parameters.AddWithValue("$lastSyncedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(resetEvent.LastSyncedAtUtc));
        command.ExecuteNonQuery();
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
