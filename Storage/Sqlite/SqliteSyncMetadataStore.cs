using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Stranichnik.Sync.Local;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSyncMetadataStore : ISyncMetadataStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSyncMetadataStore(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connectionFactory = connectionFactory;
    }

    public SyncLocalIdentity LoadLocalIdentity()
    {
        using var connection = _connectionFactory.OpenConnection();

        return new SyncLocalIdentity(
            ReadRequiredMetaValue(connection, "database_id"),
            ReadRequiredMetaValue(connection, "device_id"));
    }

    public IReadOnlyList<SyncPendingAssetRefRecord> LoadPendingAssetRefs()
    {
        using var connection = _connectionFactory.OpenConnection();
        return LoadPendingAssetRefs(connection, transaction: null);
    }

    internal static IReadOnlyList<SyncPendingAssetRefRecord> LoadPendingAssetRefs(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                item_id,
                asset_kind,
                remote_asset_id,
                source_hash_algorithm,
                source_hash,
                created_at_utc,
                last_attempt_at_utc,
                attempt_count,
                last_error_code
            FROM sync_pending_asset_refs
            ORDER BY created_at_utc ASC, id ASC;
            """;

        var refs = new List<SyncPendingAssetRefRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            refs.Add(ReadPendingAssetRef(reader));

        return refs;
    }

    public void UpsertPendingAssetRef(SyncPendingAssetRefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = _connectionFactory.OpenConnection();
        UpsertPendingAssetRef(connection, transaction: null, record);
    }

    internal static void UpsertPendingAssetRef(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SyncPendingAssetRefRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_pending_asset_refs (
                id,
                item_id,
                asset_kind,
                remote_asset_id,
                source_hash_algorithm,
                source_hash,
                created_at_utc,
                last_attempt_at_utc,
                attempt_count,
                last_error_code)
            VALUES (
                $id,
                $itemId,
                $assetKind,
                $remoteAssetId,
                $sourceHashAlgorithm,
                $sourceHash,
                $createdAtUtc,
                $lastAttemptAtUtc,
                $attemptCount,
                $lastErrorCode)
            ON CONFLICT(item_id, asset_kind) DO UPDATE SET
                id = excluded.id,
                remote_asset_id = excluded.remote_asset_id,
                source_hash_algorithm = excluded.source_hash_algorithm,
                source_hash = excluded.source_hash,
                created_at_utc = excluded.created_at_utc,
                last_attempt_at_utc = excluded.last_attempt_at_utc,
                attempt_count = excluded.attempt_count,
                last_error_code = excluded.last_error_code;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$itemId", record.ItemId);
        command.Parameters.AddWithValue("$assetKind", ToDatabaseValue(record.AssetKind));
        command.Parameters.AddWithValue("$remoteAssetId", record.RemoteAssetId);
        command.Parameters.AddWithValue("$sourceHashAlgorithm", SqliteBookmarkItemMapper.ToDatabaseValue(record.SourceHashAlgorithm));
        command.Parameters.AddWithValue("$sourceHash", SqliteBookmarkItemMapper.ToDatabaseValue(record.SourceHash));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$lastAttemptAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(record.LastAttemptAtUtc));
        command.Parameters.AddWithValue("$attemptCount", record.AttemptCount);
        command.Parameters.AddWithValue("$lastErrorCode", SqliteBookmarkItemMapper.ToDatabaseValue(record.LastErrorCode));
        command.ExecuteNonQuery();
    }

    public void DeletePendingAssetRef(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be empty.", nameof(id));

        using var connection = _connectionFactory.OpenConnection();
        DeletePendingAssetRef(connection, transaction: null, id);
    }

    internal static void DeletePendingAssetRef(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be empty.", nameof(id));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sync_pending_asset_refs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeletePendingAssetRefForItem(string itemId, SyncPendingAssetKind assetKind)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));

        using var connection = _connectionFactory.OpenConnection();
        DeletePendingAssetRefForItem(connection, transaction: null, itemId, assetKind);
    }

    internal static void DeletePendingAssetRefForItem(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string itemId,
        SyncPendingAssetKind assetKind)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sync_pending_asset_refs
            WHERE item_id = $itemId
                AND asset_kind = $assetKind;
            """;
        command.Parameters.AddWithValue("$itemId", itemId);
        command.Parameters.AddWithValue("$assetKind", ToDatabaseValue(assetKind));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SyncDeferredSecretItemRecord> LoadDeferredSecretItems()
    {
        using var connection = _connectionFactory.OpenConnection();
        return LoadDeferredSecretItems(connection, transaction: null);
    }

    internal static IReadOnlyList<SyncDeferredSecretItemRecord> LoadDeferredSecretItems(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                remote_item_id,
                secret_generation_id,
                remote_etag,
                content_hash,
                canonical_json,
                created_at_utc,
                last_attempt_at_utc,
                attempt_count,
                last_error_code
            FROM sync_deferred_secret_items
            ORDER BY created_at_utc ASC, remote_item_id ASC;
            """;

        var items = new List<SyncDeferredSecretItemRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            items.Add(ReadDeferredSecretItem(reader));

        return items;
    }

    public void UpsertDeferredSecretItem(SyncDeferredSecretItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = _connectionFactory.OpenConnection();
        UpsertDeferredSecretItem(connection, transaction: null, record);
    }

    internal static void UpsertDeferredSecretItem(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SyncDeferredSecretItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_deferred_secret_items (
                remote_item_id,
                secret_generation_id,
                remote_etag,
                content_hash,
                canonical_json,
                created_at_utc,
                last_attempt_at_utc,
                attempt_count,
                last_error_code)
            VALUES (
                $remoteItemId,
                $secretGenerationId,
                $remoteEtag,
                $contentHash,
                $canonicalJson,
                $createdAtUtc,
                $lastAttemptAtUtc,
                $attemptCount,
                $lastErrorCode)
            ON CONFLICT(remote_item_id) DO UPDATE SET
                secret_generation_id = excluded.secret_generation_id,
                remote_etag = excluded.remote_etag,
                content_hash = excluded.content_hash,
                canonical_json = excluded.canonical_json,
                created_at_utc = excluded.created_at_utc,
                last_attempt_at_utc = excluded.last_attempt_at_utc,
                attempt_count = excluded.attempt_count,
                last_error_code = excluded.last_error_code;
            """;
        command.Parameters.AddWithValue("$remoteItemId", record.RemoteItemId);
        command.Parameters.AddWithValue("$secretGenerationId", record.SecretGenerationId);
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(record.RemoteEtag));
        command.Parameters.AddWithValue("$contentHash", record.ContentHash);
        command.Parameters.AddWithValue("$canonicalJson", record.CanonicalJson.ToArray());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$lastAttemptAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(record.LastAttemptAtUtc));
        command.Parameters.AddWithValue("$attemptCount", record.AttemptCount);
        command.Parameters.AddWithValue("$lastErrorCode", SqliteBookmarkItemMapper.ToDatabaseValue(record.LastErrorCode));
        command.ExecuteNonQuery();
    }

    public void DeleteDeferredSecretItem(string remoteItemId)
    {
        if (string.IsNullOrWhiteSpace(remoteItemId))
            throw new ArgumentException("Remote item ID cannot be empty.", nameof(remoteItemId));

        using var connection = _connectionFactory.OpenConnection();
        DeleteDeferredSecretItem(connection, transaction: null, remoteItemId);
    }

    internal static void DeleteDeferredSecretItem(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string remoteItemId)
    {
        if (string.IsNullOrWhiteSpace(remoteItemId))
            throw new ArgumentException("Remote item ID cannot be empty.", nameof(remoteItemId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sync_deferred_secret_items WHERE remote_item_id = $remoteItemId;";
        command.Parameters.AddWithValue("$remoteItemId", remoteItemId);
        command.ExecuteNonQuery();
    }

    public void DeleteDeferredSecretItemsForGeneration(string secretGenerationId)
    {
        using var connection = _connectionFactory.OpenConnection();
        DeleteDeferredSecretItemsForGeneration(connection, transaction: null, secretGenerationId);
    }

    internal static void DeleteDeferredSecretItemsForGeneration(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string secretGenerationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sync_deferred_secret_items
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SyncQuarantinedRemoteObjectRecord> LoadQuarantinedRemoteObjects()
    {
        using var connection = _connectionFactory.OpenConnection();
        return LoadQuarantinedRemoteObjects(connection, transaction: null);
    }

    internal static IReadOnlyList<SyncQuarantinedRemoteObjectRecord> LoadQuarantinedRemoteObjects(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                object_kind,
                relative_path,
                remote_etag,
                content_hash,
                reason_code,
                first_seen_at_utc,
                last_seen_at_utc,
                seen_count
            FROM sync_quarantined_remote_objects
            ORDER BY last_seen_at_utc DESC, id ASC;
            """;

        var objects = new List<SyncQuarantinedRemoteObjectRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            objects.Add(ReadQuarantinedRemoteObject(reader));

        return objects;
    }

    public void UpsertQuarantinedRemoteObject(SyncQuarantinedRemoteObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = _connectionFactory.OpenConnection();
        UpsertQuarantinedRemoteObject(connection, transaction: null, record);
    }

    internal static void UpsertQuarantinedRemoteObject(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SyncQuarantinedRemoteObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_quarantined_remote_objects (
                id,
                object_kind,
                relative_path,
                remote_etag,
                content_hash,
                reason_code,
                first_seen_at_utc,
                last_seen_at_utc,
                seen_count)
            VALUES (
                $id,
                $objectKind,
                $relativePath,
                $remoteEtag,
                $contentHash,
                $reasonCode,
                $firstSeenAtUtc,
                $lastSeenAtUtc,
                $seenCount)
            ON CONFLICT(id) DO UPDATE SET
                object_kind = excluded.object_kind,
                relative_path = excluded.relative_path,
                remote_etag = excluded.remote_etag,
                content_hash = excluded.content_hash,
                reason_code = excluded.reason_code,
                first_seen_at_utc = sync_quarantined_remote_objects.first_seen_at_utc,
                last_seen_at_utc = excluded.last_seen_at_utc,
                seen_count = sync_quarantined_remote_objects.seen_count + 1;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$objectKind", record.ObjectKind);
        command.Parameters.AddWithValue("$relativePath", record.RelativePath);
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(record.RemoteEtag));
        command.Parameters.AddWithValue("$contentHash", SqliteBookmarkItemMapper.ToDatabaseValue(record.ContentHash));
        command.Parameters.AddWithValue("$reasonCode", record.ReasonCode);
        command.Parameters.AddWithValue("$firstSeenAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.FirstSeenAtUtc));
        command.Parameters.AddWithValue("$lastSeenAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.LastSeenAtUtc));
        command.Parameters.AddWithValue("$seenCount", record.SeenCount);
        command.ExecuteNonQuery();
    }

    public void DeleteQuarantinedRemoteObject(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be empty.", nameof(id));

        using var connection = _connectionFactory.OpenConnection();
        DeleteQuarantinedRemoteObject(connection, transaction: null, id);
    }

    internal static void DeleteQuarantinedRemoteObject(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be empty.", nameof(id));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sync_quarantined_remote_objects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static SyncPendingAssetRefRecord ReadPendingAssetRef(SqliteDataReader reader)
    {
        return new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("item_id")),
            ToPendingAssetKind(reader.GetString(reader.GetOrdinal("asset_kind"))),
            reader.GetString(reader.GetOrdinal("remote_asset_id")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "source_hash_algorithm"),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "source_hash"),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))),
            SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "last_attempt_at_utc"),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "last_error_code"));
    }

    private static SyncDeferredSecretItemRecord ReadDeferredSecretItem(SqliteDataReader reader)
    {
        return new(
            reader.GetString(reader.GetOrdinal("remote_item_id")),
            reader.GetString(reader.GetOrdinal("secret_generation_id")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "remote_etag"),
            reader.GetString(reader.GetOrdinal("content_hash")),
            SqliteBookmarkItemMapper.ReadNullableBytes(reader, "canonical_json")
                ?? throw new InvalidOperationException("Deferred secret item payload is missing."),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))),
            SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "last_attempt_at_utc"),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "last_error_code"));
    }

    private static SyncQuarantinedRemoteObjectRecord ReadQuarantinedRemoteObject(SqliteDataReader reader)
    {
        return new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("object_kind")),
            reader.GetString(reader.GetOrdinal("relative_path")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "remote_etag"),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "content_hash"),
            reader.GetString(reader.GetOrdinal("reason_code")),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("first_seen_at_utc"))),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("last_seen_at_utc"))),
            reader.GetInt32(reader.GetOrdinal("seen_count")));
    }

    private static string ToDatabaseValue(SyncPendingAssetKind kind)
    {
        return kind switch
        {
            SyncPendingAssetKind.RegularIcon => "regular-icon",
            SyncPendingAssetKind.SecretIcon => "secret-icon",
            _ => throw new InvalidOperationException("Unsupported pending asset kind.")
        };
    }

    private static SyncPendingAssetKind ToPendingAssetKind(string value)
    {
        return value switch
        {
            "regular-icon" => SyncPendingAssetKind.RegularIcon,
            "secret-icon" => SyncPendingAssetKind.SecretIcon,
            _ => throw new InvalidOperationException("Unsupported pending asset kind.")
        };
    }

    private static string ReadRequiredMetaValue(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value
            FROM app_meta
            WHERE key = $key;
            """;
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("SQLite sync metadata is missing.");
    }
}
