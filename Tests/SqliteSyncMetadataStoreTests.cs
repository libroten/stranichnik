using System;
using System.IO;
using Stranichnik.Storage.Sqlite;
using Stranichnik.Sync.Local;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteSyncMetadataStoreTests
{
    [Fact]
    public void PendingAssetRefs_round_trip_and_delete()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertBookmark("bookmark");
        var record = new SyncPendingAssetRefRecord(
            "pending",
            "bookmark",
            SyncPendingAssetKind.RegularIcon,
            "remote-icon",
            "sha256",
            "source-hash",
            CreatedAt,
            LastAttemptAt,
            AttemptCount: 2,
            LastErrorCode: "missing");

        database.Store.UpsertPendingAssetRef(record);

        var loaded = Assert.Single(database.Store.LoadPendingAssetRefs());
        Assert.Equal(record, loaded);

        database.Store.DeletePendingAssetRef("pending");

        Assert.Empty(database.Store.LoadPendingAssetRefs());
    }

    [Fact]
    public void PendingAssetRefs_upsert_replaces_existing_item_kind_reference()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertBookmark("bookmark");
        database.Store.UpsertPendingAssetRef(CreatePendingAssetRef("first", "first-remote"));

        database.Store.UpsertPendingAssetRef(CreatePendingAssetRef("second", "second-remote"));

        var loaded = Assert.Single(database.Store.LoadPendingAssetRefs());
        Assert.Equal("second", loaded.Id);
        Assert.Equal("second-remote", loaded.RemoteAssetId);
    }

    [Fact]
    public void PendingAssetRefs_can_delete_by_item_and_kind()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertBookmark("bookmark");
        database.Store.UpsertPendingAssetRef(CreatePendingAssetRef("pending", "remote-icon"));

        database.Store.DeletePendingAssetRefForItem("bookmark", SyncPendingAssetKind.RegularIcon);

        Assert.Empty(database.Store.LoadPendingAssetRefs());
    }

    [Fact]
    public void DeferredSecretItems_round_trip_and_delete()
    {
        using var database = TempSqliteDatabase.Create();
        var record = new SyncDeferredSecretItemRecord(
            "remote-secret",
            "generation",
            "etag",
            "sha256:content",
            DeferredJson,
            CreatedAt,
            LastAttemptAt,
            AttemptCount: 3,
            LastErrorCode: "profile-missing");

        database.Store.UpsertDeferredSecretItem(record);

        var loaded = Assert.Single(database.Store.LoadDeferredSecretItems());
        Assert.Equal(record.RemoteItemId, loaded.RemoteItemId);
        Assert.Equal(record.SecretGenerationId, loaded.SecretGenerationId);
        Assert.Equal(record.RemoteEtag, loaded.RemoteEtag);
        Assert.Equal(record.ContentHash, loaded.ContentHash);
        Assert.Equal(record.CanonicalJson.ToArray(), loaded.CanonicalJson.ToArray());
        Assert.Equal(record.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(record.LastAttemptAtUtc, loaded.LastAttemptAtUtc);
        Assert.Equal(record.AttemptCount, loaded.AttemptCount);
        Assert.Equal(record.LastErrorCode, loaded.LastErrorCode);

        database.Store.DeleteDeferredSecretItem("remote-secret");

        Assert.Empty(database.Store.LoadDeferredSecretItems());
    }

    [Fact]
    public void DeferredSecretItems_can_delete_by_generation()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.UpsertDeferredSecretItem(CreateDeferredSecretItem("first", "generation"));
        database.Store.UpsertDeferredSecretItem(CreateDeferredSecretItem("second", "other-generation"));

        database.Store.DeleteDeferredSecretItemsForGeneration("generation");

        var loaded = Assert.Single(database.Store.LoadDeferredSecretItems());
        Assert.Equal("second", loaded.RemoteItemId);
    }

    [Fact]
    public void QuarantinedRemoteObjects_round_trip_and_delete()
    {
        using var database = TempSqliteDatabase.Create();
        var record = new SyncQuarantinedRemoteObjectRecord(
            "quarantine",
            "item",
            "items/item.json",
            "etag",
            "sha256:content",
            "invalid-json",
            CreatedAt,
            LastAttemptAt,
            SeenCount: 4);

        database.Store.UpsertQuarantinedRemoteObject(record);

        var loaded = Assert.Single(database.Store.LoadQuarantinedRemoteObjects());
        Assert.Equal(record, loaded);

        database.Store.DeleteQuarantinedRemoteObject("quarantine");

        Assert.Empty(database.Store.LoadQuarantinedRemoteObjects());
    }

    [Fact]
    public void QuarantinedRemoteObjects_upsert_preserves_first_seen_and_increments_count()
    {
        using var database = TempSqliteDatabase.Create();
        var first = new SyncQuarantinedRemoteObjectRecord(
            "quarantine",
            "item",
            "items/item.json",
            "etag-1",
            "sha256:first",
            "invalid-json",
            CreatedAt,
            CreatedAt,
            SeenCount: 1);
        var second = first with
        {
            RemoteEtag = "etag-2",
            ContentHash = "sha256:second",
            ReasonCode = "invalid-remote-object",
            FirstSeenAtUtc = LastAttemptAt,
            LastSeenAtUtc = LastAttemptAt,
            SeenCount = 1
        };

        database.Store.UpsertQuarantinedRemoteObject(first);
        database.Store.UpsertQuarantinedRemoteObject(second);

        var loaded = Assert.Single(database.Store.LoadQuarantinedRemoteObjects());
        Assert.Equal(CreatedAt, loaded.FirstSeenAtUtc);
        Assert.Equal(LastAttemptAt, loaded.LastSeenAtUtc);
        Assert.Equal(2, loaded.SeenCount);
        Assert.Equal("etag-2", loaded.RemoteEtag);
        Assert.Equal("sha256:second", loaded.ContentHash);
        Assert.Equal("invalid-remote-object", loaded.ReasonCode);
    }

    private static SyncPendingAssetRefRecord CreatePendingAssetRef(
        string id,
        string remoteAssetId)
    {
        return new(
            Id: id,
            ItemId: "bookmark",
            AssetKind: SyncPendingAssetKind.RegularIcon,
            RemoteAssetId: remoteAssetId,
            SourceHashAlgorithm: null,
            SourceHash: null,
            CreatedAtUtc: CreatedAt,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null);
    }

    private static SyncDeferredSecretItemRecord CreateDeferredSecretItem(
        string remoteItemId,
        string generation)
    {
        return new(
            RemoteItemId: remoteItemId,
            SecretGenerationId: generation,
            RemoteEtag: null,
            ContentHash: "sha256:content",
            CanonicalJson: DeferredJson,
            CreatedAtUtc: CreatedAt,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null);
    }

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;
        private readonly SqliteConnectionFactory _connectionFactory;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            _connectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
            new SqliteDatabaseMigrator(
                _connectionFactory,
                idFactory: () => "device-id").Migrate();
            Store = new SqliteSyncMetadataStore(_connectionFactory);
        }

        public SqliteSyncMetadataStore Store { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public void InsertBookmark(string id)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
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
                VALUES (
                    $id,
                    NULL,
                    'bookmark',
                    1000,
                    'Title',
                    'https://example.com',
                    0,
                    NULL,
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
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
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

    private static readonly byte[] DeferredJson = [1, 2, 3, 4];

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LastAttemptAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
