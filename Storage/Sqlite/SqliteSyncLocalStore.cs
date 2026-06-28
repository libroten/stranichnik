using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Stranichnik.Diagnostics;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSyncLocalStore : ISyncLocalStore
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteBookmarkTreeStore _bookmarkTreeStore;
    private readonly SqliteSecretProfileStore _secretProfileStore;
    private readonly SqliteSecretResetStore _secretResetStore;
    private readonly SqliteSyncMetadataStore _syncMetadataStore;
    private readonly ISyncJsonSerializer _serializer;
    private readonly Action<string> _log;

    public SqliteSyncLocalStore(
        SqliteConnectionFactory connectionFactory,
        SqliteBookmarkTreeStore bookmarkTreeStore,
        SqliteSecretProfileStore secretProfileStore,
        SqliteSecretResetStore secretResetStore,
        SqliteSyncMetadataStore syncMetadataStore,
        ISyncJsonSerializer serializer,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(bookmarkTreeStore);
        ArgumentNullException.ThrowIfNull(secretProfileStore);
        ArgumentNullException.ThrowIfNull(secretResetStore);
        ArgumentNullException.ThrowIfNull(syncMetadataStore);
        ArgumentNullException.ThrowIfNull(serializer);

        _connectionFactory = connectionFactory;
        _bookmarkTreeStore = bookmarkTreeStore;
        _secretProfileStore = secretProfileStore;
        _secretResetStore = secretResetStore;
        _syncMetadataStore = syncMetadataStore;
        _serializer = serializer;
        _log = log ?? Logs.Print;
    }

    public SyncLocalSnapshot LoadSnapshot()
    {
        return new SyncLocalSnapshot(
            _syncMetadataStore.LoadLocalIdentity(),
            _bookmarkTreeStore.LoadAllItemsForSync(),
            _bookmarkTreeStore.LoadAllIconAssetsForSync(),
            _bookmarkTreeStore.LoadAllSecretIconAssetsForSync(),
            _secretProfileStore.LoadAllProfilesForSync(),
            _secretResetStore.LoadResetEvents(),
            _syncMetadataStore.LoadPendingAssetRefs(),
            _syncMetadataStore.LoadDeferredSecretItems(),
            _syncMetadataStore.LoadQuarantinedRemoteObjects());
    }

    public void ApplyRemoteChanges(SyncApplyBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var syncedAtUtc = DateTimeOffset.UtcNow;
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        ApplyRemoteChanges(batch, syncedAtUtc, connection, transaction);

        transaction.Commit();
    }

    private void ApplyRemoteChanges(
        SyncApplyBatch batch,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        _log(
            "SQLite sync apply remote changes started. " +
            $"ResetEvents={batch.SecretResetEvents.Count}; " +
            $"CryptoProfiles={batch.CryptoProfiles.Count}; " +
            $"IconAssets={batch.IconAssets.Count}; " +
            $"SecretIconAssets={batch.SecretIconAssets.Count}; " +
            $"Items={batch.Items.Count}.");

        foreach (var resetEvent in batch.SecretResetEvents)
        {
            _log("SQLite sync apply remote object started. Kind=SecretResetEvent.");
            ApplyRemoteResetEvent(resetEvent, syncedAtUtc, connection, transaction);
            _log("SQLite sync apply remote object finished. Kind=SecretResetEvent.");
        }

        foreach (var iconAsset in batch.IconAssets)
        {
            _log("SQLite sync apply remote object started. Kind=IconAsset.");
            ApplyRemoteIconAsset(iconAsset, syncedAtUtc, connection, transaction);
            _log("SQLite sync apply remote object finished. Kind=IconAsset.");
        }

        foreach (var secretIconAsset in batch.SecretIconAssets)
        {
            _log("SQLite sync apply remote object started. Kind=SecretIconAsset.");
            ApplyRemoteSecretIconAsset(secretIconAsset, syncedAtUtc, connection, transaction);
            _log("SQLite sync apply remote object finished. Kind=SecretIconAsset.");
        }

        foreach (var profile in batch.CryptoProfiles)
        {
            _log("SQLite sync apply remote object started. Kind=CryptoProfile.");
            ApplyRemoteCryptoProfile(profile, syncedAtUtc, connection, transaction);
            _log("SQLite sync apply remote object finished. Kind=CryptoProfile.");
        }

        foreach (var item in OrderItemsForApply(batch.Items, connection, transaction))
        {
            _log("SQLite sync apply remote object started. Kind=Item.");
            ApplyRemoteItem(item, syncedAtUtc, connection, transaction);
            _log("SQLite sync apply remote object finished. Kind=Item.");
        }

        ReconcilePendingRemoteDependencies(syncedAtUtc, connection, transaction);
        _log("SQLite sync apply remote changes finished.");
    }

    public void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _log("SQLite sync apply pull plan started.");
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        ApplyRemoteChanges(plan.ApplyBatch, syncedAtUtc, connection, transaction);

        foreach (var matchedObject in plan.MatchedDirtyObjects)
        {
            RefreshDirtyRemoteMetadata(
                matchedObject.Identity,
                matchedObject.RemoteEtag,
                matchedObject.ContentHash,
                syncedAtUtc,
                connection,
                transaction);
        }

        foreach (var satisfiedResetEvent in plan.SatisfiedResetEvents)
        {
            MarkSyncMetadata(
                new SyncObjectIdentity(SyncObjectKind.SecretResetEvent, satisfiedResetEvent.SecretGenerationId),
                BookmarkSyncState.Clean,
                satisfiedResetEvent.RemoteEtag,
                syncedAtUtc,
                contentHash: null,
                connection,
                transaction);
            _log("SQLite sync secret reset event satisfied by remote object.");
        }

        foreach (var conflict in plan.Conflicts)
            MarkConflict(conflict.Identity, conflict.ReasonCode, connection, transaction);

        foreach (var quarantineCandidate in plan.QuarantinedRemoteObjects)
        {
            MarkQuarantinedRemoteObject(
                quarantineCandidate.ObjectKind,
                quarantineCandidate.RelativePath,
                quarantineCandidate.RemoteEtag,
                quarantineCandidate.ContentHash,
                quarantineCandidate.ReasonCode,
                syncedAtUtc,
                connection,
                transaction);
        }

        foreach (var quarantineCandidate in plan.KnownQuarantinedRemoteObjects)
        {
            MarkQuarantinedRemoteObject(
                quarantineCandidate.ObjectKind,
                quarantineCandidate.RelativePath,
                quarantineCandidate.RemoteEtag,
                quarantineCandidate.ContentHash,
                quarantineCandidate.ReasonCode,
                syncedAtUtc,
                connection,
                transaction);
        }

        foreach (var resolvedQuarantineId in plan.ResolvedQuarantinedRemoteObjectIds.Distinct(StringComparer.Ordinal))
            ClearQuarantinedRemoteObject(resolvedQuarantineId, connection, transaction);

        foreach (var missingRemoteObject in plan.MissingRemoteObjects)
            MarkRemoteMissingAsDirty(missingRemoteObject, connection, transaction);

        transaction.Commit();
        _log("SQLite sync apply pull plan finished.");
    }

    public void ClearLocalSecretsForRemoteTruth(DateTimeOffset changedAtUtc)
    {
        _ = changedAtUtc;

        _log("SQLite sync local secret cleanup for remote truth started.");
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var secretBookmarkCount = CountSecretBookmarks(connection, transaction);
        var secretIconAssetCount = CountSecretIconAssets(connection, transaction);
        var cryptoProfileCount = CountCryptoProfiles(connection, transaction);
        var deferredSecretItemCount = CountDeferredSecretItems(connection, transaction);

        var folderIdsToPurge = SelectSecretOnlyFolderIds(connection, transaction);
        DeleteAllSecretBookmarks(connection, transaction);
        DeleteFolders(connection, transaction, folderIdsToPurge);
        DeleteAllSecretIconAssets(connection, transaction);
        DeleteAllCryptoProfiles(connection, transaction);
        DeleteAllDeferredSecretItems(connection, transaction);
        DeleteAllPendingSecretIconRefs(connection, transaction);
        DeleteAllSecretResetEvents(connection, transaction);

        transaction.Commit();
        _log(
            "SQLite sync local secret cleanup for remote truth finished. " +
            $"SecretBookmarks={secretBookmarkCount}; " +
            $"SecretOnlyFolders={folderIdsToPurge.Count}; " +
            $"SecretIconAssets={secretIconAssetCount}; " +
            $"CryptoProfiles={cryptoProfileCount}; " +
            $"DeferredSecretItems={deferredSecretItemCount}.");
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

    private static int CountSecretIconAssets(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM secret_icon_assets;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int CountCryptoProfiles(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM crypto_profiles;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int CountDeferredSecretItems(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sync_deferred_secret_items;";
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
                target_secret_bookmarks(id, parent_id) AS (
                    SELECT item.id, item.parent_id
                    FROM items item
                    WHERE item.item_type = 'bookmark'
                        AND item.is_secret = 1
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

        using var reader = command.ExecuteReader();
        var folderIds = new List<string>();

        while (reader.Read())
            folderIds.Add(reader.GetString(0));

        return folderIds;
    }

    private static void DeleteAllSecretBookmarks(
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
        IReadOnlyList<string> folderIds)
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

    private static void DeleteAllSecretIconAssets(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM secret_icon_assets;";
        command.ExecuteNonQuery();
    }

    private static void DeleteAllCryptoProfiles(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM crypto_profiles;";
        command.ExecuteNonQuery();
    }

    private static void DeleteAllDeferredSecretItems(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sync_deferred_secret_items;";
        command.ExecuteNonQuery();
    }

    private static void DeleteAllPendingSecretIconRefs(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sync_pending_asset_refs
            WHERE asset_kind = 'secret-icon';
            """;
        command.ExecuteNonQuery();
    }

    private static void DeleteAllSecretResetEvents(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM secret_reset_events;";
        command.ExecuteNonQuery();
    }

    private void RefreshDirtyRemoteMetadata(
        SyncObjectIdentity identity,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        MarkSyncMetadata(
            identity,
            BookmarkSyncState.Dirty,
            remoteEtag,
            syncedAtUtc,
            contentHash,
            connection,
            transaction);
        _log($"SQLite sync dirty metadata refreshed from unchanged remote object. Kind={identity.Kind}; RemoteEtagPresent={remoteEtag is not null}.");
    }

    public void MarkUploaded(
        SyncObjectIdentity identity,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        MarkSyncMetadata(
            identity,
            BookmarkSyncState.Clean,
            remoteEtag,
            syncedAtUtc,
            contentHash);
        _log($"SQLite sync metadata marked uploaded. Kind={identity.Kind}; RemoteEtagPresent={remoteEtag is not null}.");
    }

    private void MarkUploaded(
        SyncObjectIdentity identity,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        MarkSyncMetadata(
            identity,
            BookmarkSyncState.Clean,
            remoteEtag,
            syncedAtUtc,
            contentHash,
            connection,
            transaction);
        _log($"SQLite sync metadata marked uploaded. Kind={identity.Kind}; RemoteEtagPresent={remoteEtag is not null}.");
    }

    public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        MarkSyncState(identity, BookmarkSyncState.Conflict);
        _log($"SQLite sync metadata marked conflict. Kind={identity.Kind}; Reason={reasonCode}.");
    }

    private void MarkConflict(
        SyncObjectIdentity identity,
        string reasonCode,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        MarkSyncState(identity, BookmarkSyncState.Conflict, connection, transaction);
        _log($"SQLite sync metadata marked conflict. Kind={identity.Kind}; Reason={reasonCode}.");
    }

    public void MarkDirty(SyncObjectIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        MarkSyncState(identity, BookmarkSyncState.Dirty);
        _log($"SQLite sync metadata marked dirty. Kind={identity.Kind}.");
    }

    private void MarkRemoteMissingAsDirty(SyncObjectIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var metadata = LoadMetadata(identity);
        MarkSyncMetadata(
            identity,
            BookmarkSyncState.Dirty,
            remoteEtag: null,
            metadata.LastSyncedAtUtc,
            metadata.ContentHash);
        _log($"SQLite sync metadata marked dirty for missing remote object. Kind={identity.Kind}.");
    }

    private void MarkRemoteMissingAsDirty(
        SyncObjectIdentity identity,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var metadata = LoadMetadata(identity, connection, transaction);
        MarkSyncMetadata(
            identity,
            BookmarkSyncState.Dirty,
            remoteEtag: null,
            metadata.LastSyncedAtUtc,
            metadata.ContentHash,
            connection,
            transaction);
        _log($"SQLite sync metadata marked dirty for missing remote object. Kind={identity.Kind}.");
    }

    public void MarkQuarantinedRemoteObject(
        string objectKind,
        string relativePath,
        string? remoteEtag,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        _syncMetadataStore.UpsertQuarantinedRemoteObject(new SyncQuarantinedRemoteObjectRecord(
            Id: SyncQuarantinedRemoteObjectId.FromRemotePath(relativePath),
            ObjectKind: objectKind,
            RelativePath: relativePath,
            RemoteEtag: remoteEtag,
            ContentHash: contentHash,
            ReasonCode: reasonCode,
            FirstSeenAtUtc: seenAtUtc,
            LastSeenAtUtc: seenAtUtc,
            SeenCount: 1));
        _log(
            "SQLite sync remote object quarantine upserted. " +
            $"ObjectKind={objectKind}; " +
            $"Reason={reasonCode}; " +
            $"RemoteEtagPresent={remoteEtag is not null}; " +
            $"ContentHashPresent={contentHash is not null}.");
    }

    private void MarkQuarantinedRemoteObject(
        string objectKind,
        string relativePath,
        string? remoteEtag,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        SqliteSyncMetadataStore.UpsertQuarantinedRemoteObject(
            connection,
            transaction,
            new SyncQuarantinedRemoteObjectRecord(
                Id: SyncQuarantinedRemoteObjectId.FromRemotePath(relativePath),
                ObjectKind: objectKind,
                RelativePath: relativePath,
                RemoteEtag: remoteEtag,
                ContentHash: contentHash,
                ReasonCode: reasonCode,
                FirstSeenAtUtc: seenAtUtc,
                LastSeenAtUtc: seenAtUtc,
                SeenCount: 1));
        _log(
            "SQLite sync remote object quarantine upserted. " +
            $"ObjectKind={objectKind}; " +
            $"Reason={reasonCode}; " +
            $"RemoteEtagPresent={remoteEtag is not null}; " +
            $"ContentHashPresent={contentHash is not null}.");
    }

    public void ClearQuarantinedRemoteObject(string id)
    {
        _syncMetadataStore.DeleteQuarantinedRemoteObject(id);
        _log("SQLite sync remote object quarantine cleared.");
    }

    private void ClearQuarantinedRemoteObject(
        string id,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteSyncMetadataStore.DeleteQuarantinedRemoteObject(connection, transaction, id);
        _log("SQLite sync remote object quarantine cleared.");
    }

    private void MarkSyncMetadata(
        SyncObjectIdentity identity,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc,
        string? contentHash)
    {
        switch (identity.Kind)
        {
            case SyncObjectKind.Item:
            case SyncObjectKind.IconAsset:
            case SyncObjectKind.SecretIconAsset:
                _bookmarkTreeStore.MarkSyncMetadata(
                    identity.Kind,
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc,
                    contentHash);
                break;
            case SyncObjectKind.CryptoProfile:
                _secretProfileStore.MarkSyncMetadata(
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc,
                    contentHash);
                break;
            case SyncObjectKind.SecretResetEvent:
                _secretResetStore.MarkSyncMetadata(
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.");
        }
    }

    private static void MarkSyncMetadata(
        SyncObjectIdentity identity,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc,
        string? contentHash,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        switch (identity.Kind)
        {
            case SyncObjectKind.Item:
            case SyncObjectKind.IconAsset:
            case SyncObjectKind.SecretIconAsset:
                SqliteBookmarkTreeStore.MarkSyncMetadata(
                    connection,
                    transaction,
                    identity.Kind,
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc,
                    contentHash);
                break;
            case SyncObjectKind.CryptoProfile:
                SqliteSecretProfileStore.MarkSyncMetadata(
                    connection,
                    transaction,
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc,
                    contentHash);
                break;
            case SyncObjectKind.SecretResetEvent:
                SqliteSecretResetStore.MarkSyncMetadata(
                    connection,
                    transaction,
                    identity.Id,
                    syncState,
                    remoteEtag,
                    lastSyncedAtUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.");
        }
    }

    private SyncObjectMetadata LoadMetadata(SyncObjectIdentity identity)
    {
        var snapshot = LoadSnapshot();
        return identity.Kind switch
        {
            SyncObjectKind.Item => snapshot.Items
                .Single(item => string.Equals(item.Item.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.IconAsset => snapshot.IconAssets
                .Single(asset => string.Equals(asset.Asset.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.SecretIconAsset => snapshot.SecretIconAssets
                .Single(asset => string.Equals(asset.Asset.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.CryptoProfile => snapshot.CryptoProfiles
                .Single(profile => string.Equals(profile.Profile.SecretGenerationId, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.SecretResetEvent => snapshot.SecretResetEvents
                .Where(resetEvent => string.Equals(resetEvent.SecretGenerationId, identity.Id, StringComparison.Ordinal))
                .Select(resetEvent => new SyncObjectMetadata(
                    resetEvent.SyncState,
                    resetEvent.RemoteEtag,
                    resetEvent.LastSyncedAtUtc,
                    ContentHash: null,
                    resetEvent.ResetDeviceId))
                .Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.")
        };
    }

    private static SyncObjectMetadata LoadMetadata(
        SyncObjectIdentity identity,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        return identity.Kind switch
        {
            SyncObjectKind.Item => SqliteBookmarkTreeStore
                .LoadAllItemsForSync(connection, transaction)
                .Single(item => string.Equals(item.Item.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.IconAsset => SqliteBookmarkTreeStore
                .LoadAllIconAssetsForSync(connection, transaction)
                .Single(asset => string.Equals(asset.Asset.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.SecretIconAsset => SqliteBookmarkTreeStore
                .LoadAllSecretIconAssetsForSync(connection, transaction)
                .Single(asset => string.Equals(asset.Asset.Id, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.CryptoProfile => SqliteSecretProfileStore
                .LoadAllProfilesForSync(connection, transaction)
                .Single(profile => string.Equals(profile.Profile.SecretGenerationId, identity.Id, StringComparison.Ordinal))
                .SyncMetadata,
            SyncObjectKind.SecretResetEvent => SqliteSecretResetStore
                .LoadResetEvents(connection, transaction)
                .Where(resetEvent => string.Equals(resetEvent.SecretGenerationId, identity.Id, StringComparison.Ordinal))
                .Select(resetEvent => new SyncObjectMetadata(
                    resetEvent.SyncState,
                    resetEvent.RemoteEtag,
                    resetEvent.LastSyncedAtUtc,
                    ContentHash: null,
                    resetEvent.ResetDeviceId))
                .Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.")
        };
    }

    private void MarkSyncState(
        SyncObjectIdentity identity,
        BookmarkSyncState syncState)
    {
        switch (identity.Kind)
        {
            case SyncObjectKind.Item:
            case SyncObjectKind.IconAsset:
            case SyncObjectKind.SecretIconAsset:
                _bookmarkTreeStore.MarkSyncState(identity.Kind, identity.Id, syncState);
                break;
            case SyncObjectKind.CryptoProfile:
                _secretProfileStore.MarkSyncState(identity.Id, syncState);
                break;
            case SyncObjectKind.SecretResetEvent:
                _secretResetStore.MarkSyncState(identity.Id, syncState);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.");
        }
    }

    private static void MarkSyncState(
        SyncObjectIdentity identity,
        BookmarkSyncState syncState,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        switch (identity.Kind)
        {
            case SyncObjectKind.Item:
            case SyncObjectKind.IconAsset:
            case SyncObjectKind.SecretIconAsset:
                SqliteBookmarkTreeStore.MarkSyncState(connection, transaction, identity.Kind, identity.Id, syncState);
                break;
            case SyncObjectKind.CryptoProfile:
                SqliteSecretProfileStore.MarkSyncState(connection, transaction, identity.Id, syncState);
                break;
            case SyncObjectKind.SecretResetEvent:
                SqliteSecretResetStore.MarkSyncState(connection, transaction, identity.Id, syncState);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity), identity.Kind, "Unsupported sync object kind.");
        }
    }

    private static void ApplyRemoteIconAsset(
        SyncAppliedRemoteObject<SyncIconAssetDto> remoteIconAsset,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteIconAsset.Value;
        var iconAsset = new BookmarkIconAssetRecord(
            value.Id,
            value.SourceHashAlgorithm,
            value.SourceHash,
            value.SourceSizeBytes,
            value.ProcessedMimeType,
            value.ProcessedWidth,
            value.ProcessedHeight,
            Convert.FromBase64String(value.ProcessedBytes),
            value.CreatedAtUtc);

        SqliteBookmarkTreeStore.UpsertRemoteIconAsset(
            connection,
            transaction,
            iconAsset,
            CreateCleanSyncMetadata(
                remoteIconAsset.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ResolvePendingAssetRefs(SyncPendingAssetKind.RegularIcon, value.Id, connection, transaction);
    }

    private static void ApplyRemoteSecretIconAsset(
        SyncAppliedRemoteObject<SyncSecretIconAssetDto> remoteSecretIconAsset,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteSecretIconAsset.Value;
        if (IsSecretGenerationReset(value.SecretGenerationId, connection, transaction))
            return;

        var iconAsset = new SecretIconAssetRecord(
            value.Id,
            value.SourceHashAlgorithm,
            value.SourceHash,
            value.SourceSizeBytes,
            value.ProcessedMimeType,
            value.ProcessedWidth,
            value.ProcessedHeight,
            new EncryptedSecretIconPayloadRecord(
                Convert.FromBase64String(value.EncryptedProcessedBytes),
                Convert.FromBase64String(value.EncryptionNonce),
                value.PayloadFormatVersion),
            value.SecretGenerationId,
            value.CreatedAtUtc);

        SqliteBookmarkTreeStore.UpsertRemoteSecretIconAsset(
            connection,
            transaction,
            iconAsset,
            CreateCleanSyncMetadata(
                remoteSecretIconAsset.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ResolvePendingAssetRefs(SyncPendingAssetKind.SecretIcon, value.Id, connection, transaction);
    }

    private static void ApplyRemoteResetEvent(
        SyncAppliedRemoteObject<SyncSecretResetEventDto> remoteResetEvent,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteResetEvent.Value;
        var resetEvent = new SecretResetEventRecord(
            value.SecretGenerationId,
            value.SecretGenerationId,
            value.ResetAtUtc,
            value.ResetDeviceId,
            BookmarkSyncState.Clean,
            remoteResetEvent.RemoteInfo.ETag,
            syncedAtUtc);

        SqliteSecretResetStore.ApplyRemoteResetEventAndPurgeSecrets(connection, transaction, resetEvent);
        SqliteSyncMetadataStore.DeleteDeferredSecretItemsForGeneration(connection, transaction, value.SecretGenerationId);
    }

    private void ApplyRemoteCryptoProfile(
        SyncAppliedRemoteObject<SyncCryptoProfileDto> remoteProfile,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteProfile.Value;
        if (IsSecretGenerationReset(value.SecretGenerationId, connection, transaction))
            return;

        var activeProfile = SqliteSecretProfileStore.LoadActiveProfile(connection, transaction);
        if (activeProfile is not null &&
            !string.Equals(activeProfile.SecretGenerationId, value.SecretGenerationId, StringComparison.Ordinal))
        {
            _log("SQLite sync remote crypto profile skipped: active local generation differs.");
            return;
        }

        var profile = new CryptoProfileRecord(
            SecretCryptoProfileIds.ActiveProfileId,
            value.ProfileVersion,
            value.KdfAlgorithm,
            SecretEncryptionConstants.KdfHashAlgorithm,
            value.KdfIterations,
            Convert.FromBase64String(value.Salt),
            SecretEncryptionConstants.KekLengthBytes,
            SecretEncryptionConstants.DataKeyAlgorithm,
            Convert.FromBase64String(value.EncryptedDataKey),
            Convert.FromBase64String(value.DataKeyNonce),
            SecretEncryptionConstants.EncryptionAlgorithm,
            SecretEncryptionConstants.PayloadFormat,
            Convert.FromBase64String(value.PasswordCheckPayload),
            Convert.FromBase64String(value.PasswordCheckNonce),
            value.CreatedAtUtc,
            value.UpdatedAtUtc,
            value.SecretGenerationId);

        SqliteSecretProfileStore.UpsertRemoteProfile(
            connection,
            transaction,
            profile,
            CreateCleanSyncMetadata(
                remoteProfile.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ApplyDeferredSecretItemsForProfile(profile, syncedAtUtc, connection, transaction);
    }

    private void ApplyRemoteItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteItem.Value;
        if (TryGetRemoteItemGraphProblem(value, connection, transaction, out var graphProblemReasonCode))
        {
            _log($"Sync remote item quarantined: invalid item graph. ReasonCode={graphProblemReasonCode}.");
            QuarantineRemoteObject(
                remoteItem.RemoteInfo,
                remoteItem.Identity,
                value.ContentHash,
                graphProblemReasonCode,
                syncedAtUtc,
                connection,
                transaction);
            return;
        }

        if (value.IsSecret)
        {
            ApplyOrDeferRemoteSecretItem(remoteItem, syncedAtUtc, connection, transaction);
            return;
        }

        var iconAssetId = ResolveRegularIconAssetId(value, connection, transaction);
        var item = new BookmarkItemRecord(
            value.Id,
            value.ParentId,
            ToBookmarkItemKind(value.Kind),
            value.SortOrder,
            value.Title,
            value.Url,
            IsSecret: false,
            EncryptedPayload: null,
            SecretGenerationId: null,
            new BookmarkItemMetadata(
                value.CreatedAtUtc,
                value.UpdatedAtUtc,
                value.DeletedAtUtc,
                value.Revision,
                BookmarkSyncState.Clean,
                remoteItem.RemoteInfo.ETag,
                syncedAtUtc,
                value.ContentHash,
                value.ModifiedDeviceId),
            iconAssetId,
            SecretIconAssetId: null);

        SqliteBookmarkTreeStore.UpsertRemoteItem(connection, transaction, item);
        UpdatePendingRegularIconRef(value, syncedAtUtc, iconAssetId, connection, transaction);
    }

    private static List<SyncAppliedRemoteObject<SyncItemDto>> OrderItemsForApply(
        IReadOnlyList<SyncAppliedRemoteObject<SyncItemDto>> items,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var remaining = items.ToList();
        var ordered = new List<SyncAppliedRemoteObject<SyncItemDto>>(remaining.Count);
        var availableIds = SqliteBookmarkTreeStore
            .LoadAllItemsForSync(connection, transaction)
            .Where(item => item.Item.Metadata.DeletedAtUtc is null)
            .Select(item => item.Item.Id)
            .ToHashSet(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var movedAny = false;

            for (var index = remaining.Count - 1; index >= 0; index--)
            {
                var item = remaining[index];
                var parentId = item.Value.ParentId;
                if (parentId is not null && !availableIds.Contains(parentId))
                    continue;

                ordered.Add(item);
                availableIds.Add(item.Value.Id);
                remaining.RemoveAt(index);
                movedAny = true;
            }

            if (!movedAny)
            {
                ordered.AddRange(remaining);
                break;
            }
        }

        return ordered;
    }

    private static bool TryGetRemoteItemGraphProblem(
        SyncItemDto item,
        SqliteConnection connection,
        SqliteTransaction transaction,
        out string reasonCode)
    {
        reasonCode = string.Empty;

        if (item.DeletedAtUtc is not null)
        {
            if (item.ParentId is not null &&
                !SqliteBookmarkTreeStore.ItemExistsIncludingDeletedForSync(connection, transaction, item.ParentId))
            {
                reasonCode = "missing-parent";
                return true;
            }

            return false;
        }

        if (string.Equals(item.ParentId, item.Id, StringComparison.Ordinal))
        {
            reasonCode = "parent-cycle";
            return true;
        }

        if (item.ParentId is not null)
        {
            if (!SqliteBookmarkTreeStore.TryGetLiveItemForSync(connection, transaction, item.ParentId, out var parent))
            {
                reasonCode = "missing-parent";
                return true;
            }

            if (parent.Kind != BookmarkItemKind.Folder)
            {
                reasonCode = "invalid-parent";
                return true;
            }
        }

        var kind = ToBookmarkItemKind(item.Kind);
        if (kind == BookmarkItemKind.Folder &&
            SqliteBookmarkTreeStore.WouldCreateCycleForSync(connection, transaction, item.Id, item.ParentId))
        {
            reasonCode = "parent-cycle";
            return true;
        }

        if (kind == BookmarkItemKind.Bookmark &&
            SqliteBookmarkTreeStore.HasLiveChildrenForSync(connection, transaction, item.Id))
        {
            reasonCode = "kind-change-with-children";
            return true;
        }

        return false;
    }

    private static string? ResolveRegularIconAssetId(
        SyncItemDto item,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (item.DeletedAtUtc is not null || item.IconAssetRef is null)
            return null;

        return SqliteBookmarkTreeStore.GetIconAsset(connection, transaction, item.IconAssetRef.AssetId) is null
            ? null
            : item.IconAssetRef.AssetId;
    }

    private static void UpdatePendingRegularIconRef(
        SyncItemDto item,
        DateTimeOffset syncedAtUtc,
        string? resolvedIconAssetId,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (item.DeletedAtUtc is not null || item.IconAssetRef is null || resolvedIconAssetId is not null)
        {
            SqliteSyncMetadataStore.DeletePendingAssetRefForItem(
                connection,
                transaction,
                item.Id,
                SyncPendingAssetKind.RegularIcon);
            return;
        }

        SqliteSyncMetadataStore.UpsertPendingAssetRef(
            connection,
            transaction,
            new SyncPendingAssetRefRecord(
                Id: $"{item.Id}:regular-icon",
                ItemId: item.Id,
                AssetKind: SyncPendingAssetKind.RegularIcon,
                RemoteAssetId: item.IconAssetRef.AssetId,
                SourceHashAlgorithm: item.IconAssetRef.SourceHashAlgorithm,
                SourceHash: item.IconAssetRef.SourceHash,
                CreatedAtUtc: syncedAtUtc,
                LastAttemptAtUtc: null,
                AttemptCount: 0,
                LastErrorCode: null));
    }

    private void ApplyOrDeferRemoteSecretItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteItem.Value;
        if (string.IsNullOrWhiteSpace(value.CryptoProfileSecretGenerationId))
            throw new InvalidOperationException("Remote secret item is missing crypto profile generation.");

        if (IsSecretGenerationReset(value.CryptoProfileSecretGenerationId, connection, transaction))
        {
            SqliteSyncMetadataStore.DeleteDeferredSecretItem(connection, transaction, value.Id);
            return;
        }

        var profile = SqliteSecretProfileStore.LoadActiveProfile(connection, transaction);
        if (profile is not null &&
            string.Equals(
                profile.SecretGenerationId,
                value.CryptoProfileSecretGenerationId,
                StringComparison.Ordinal))
        {
            ApplyRemoteSecretItem(remoteItem, syncedAtUtc, profile, connection, transaction);
            SqliteSyncMetadataStore.DeleteDeferredSecretItem(connection, transaction, value.Id);
            return;
        }

        DeferRemoteSecretItem(remoteItem, syncedAtUtc, connection, transaction);
    }

    private static void ApplyRemoteSecretItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc,
        CryptoProfileRecord profile,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteItem.Value;
        if (string.IsNullOrWhiteSpace(value.EncryptedPayload) ||
            string.IsNullOrWhiteSpace(value.EncryptionNonce) ||
            value.SecretPayloadFormatVersion is null)
        {
            throw new InvalidOperationException("Remote secret item encrypted payload is incomplete.");
        }

        if (IsSecretGenerationReset(profile.SecretGenerationId, connection, transaction))
            return;

        var secretIconAssetId = ResolveSecretIconAssetId(value, connection, transaction);
        var item = new BookmarkItemRecord(
            value.Id,
            value.ParentId,
            ToBookmarkItemKind(value.Kind),
            value.SortOrder,
            Title: null,
            Url: null,
            IsSecret: true,
            new EncryptedBookmarkPayloadRecord(
                Convert.FromBase64String(value.EncryptedPayload),
                Convert.FromBase64String(value.EncryptionNonce),
                profile.Id,
                value.SecretPayloadFormatVersion.Value),
            value.CryptoProfileSecretGenerationId,
            new BookmarkItemMetadata(
                value.CreatedAtUtc,
                value.UpdatedAtUtc,
                value.DeletedAtUtc,
                value.Revision,
                BookmarkSyncState.Clean,
                remoteItem.RemoteInfo.ETag,
                syncedAtUtc,
                value.ContentHash,
                value.ModifiedDeviceId),
            IconAssetId: null,
            SecretIconAssetId: secretIconAssetId);

        SqliteBookmarkTreeStore.UpsertRemoteItem(connection, transaction, item);
        UpdatePendingSecretIconRef(value, syncedAtUtc, secretIconAssetId, connection, transaction);
    }

    private static string? ResolveSecretIconAssetId(
        SyncItemDto item,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (item.DeletedAtUtc is not null || item.SecretIconAssetRef is null)
            return null;

        return SqliteBookmarkTreeStore.GetSecretIconAsset(connection, transaction, item.SecretIconAssetRef.AssetId) is null
            ? null
            : item.SecretIconAssetRef.AssetId;
    }

    private static void UpdatePendingSecretIconRef(
        SyncItemDto item,
        DateTimeOffset syncedAtUtc,
        string? resolvedSecretIconAssetId,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (item.DeletedAtUtc is not null || item.SecretIconAssetRef is null || resolvedSecretIconAssetId is not null)
        {
            SqliteSyncMetadataStore.DeletePendingAssetRefForItem(
                connection,
                transaction,
                item.Id,
                SyncPendingAssetKind.SecretIcon);
            return;
        }

        SqliteSyncMetadataStore.UpsertPendingAssetRef(
            connection,
            transaction,
            new SyncPendingAssetRefRecord(
                Id: $"{item.Id}:secret-icon",
                ItemId: item.Id,
                AssetKind: SyncPendingAssetKind.SecretIcon,
                RemoteAssetId: item.SecretIconAssetRef.AssetId,
                SourceHashAlgorithm: item.SecretIconAssetRef.SourceHashAlgorithm,
                SourceHash: item.SecretIconAssetRef.SourceHash,
                CreatedAtUtc: syncedAtUtc,
                LastAttemptAtUtc: null,
                AttemptCount: 0,
                LastErrorCode: null));
    }

    private void ReconcilePendingRemoteDependencies(
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var resolvedAssetRefs = 0;
        foreach (var pendingRef in SqliteSyncMetadataStore.LoadPendingAssetRefs(connection, transaction))
        {
            if (ResolvePendingAssetRef(pendingRef, connection, transaction))
                resolvedAssetRefs++;
        }

        var appliedDeferredSecretItems = 0;
        var profile = SqliteSecretProfileStore.LoadActiveProfile(connection, transaction);
        if (profile is not null && !IsSecretGenerationReset(profile.SecretGenerationId, connection, transaction))
        {
            appliedDeferredSecretItems = ApplyDeferredSecretItemsForProfile(
                profile,
                syncedAtUtc,
                connection,
                transaction);
        }

        _log(
            "SQLite sync pending dependency reconciliation finished. " +
            $"ResolvedAssetRefs={resolvedAssetRefs}; " +
            $"AppliedDeferredSecretItems={appliedDeferredSecretItems}.");
    }

    private static int ResolvePendingAssetRefs(
        SyncPendingAssetKind assetKind,
        string remoteAssetId,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var resolvedCount = 0;
        foreach (var pendingRef in SqliteSyncMetadataStore.LoadPendingAssetRefs(connection, transaction))
        {
            if (pendingRef.AssetKind != assetKind ||
                !string.Equals(pendingRef.RemoteAssetId, remoteAssetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (ResolvePendingAssetRef(pendingRef, connection, transaction))
                resolvedCount++;
        }

        return resolvedCount;
    }

    private static bool ResolvePendingAssetRef(
        SyncPendingAssetRefRecord pendingRef,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (!RemoteAssetExists(pendingRef.AssetKind, pendingRef.RemoteAssetId, connection, transaction))
            return false;

        if (!SqliteBookmarkTreeStore.TrySetRemoteResolvedIconAsset(
                connection,
                transaction,
                pendingRef.ItemId,
                pendingRef.AssetKind,
                pendingRef.RemoteAssetId))
        {
            return false;
        }

        SqliteSyncMetadataStore.DeletePendingAssetRef(connection, transaction, pendingRef.Id);
        return true;
    }

    private static bool RemoteAssetExists(
        SyncPendingAssetKind assetKind,
        string remoteAssetId,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        return assetKind switch
        {
            SyncPendingAssetKind.RegularIcon => SqliteBookmarkTreeStore.GetIconAsset(connection, transaction, remoteAssetId) is not null,
            SyncPendingAssetKind.SecretIcon => SqliteBookmarkTreeStore.GetSecretIconAsset(connection, transaction, remoteAssetId) is not null,
            _ => false
        };
    }

    private static void QuarantineRemoteObject(
        SyncRemoteObjectInfo remoteInfo,
        SyncObjectIdentity identity,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteSyncMetadataStore.UpsertQuarantinedRemoteObject(
            connection,
            transaction,
            new SyncQuarantinedRemoteObjectRecord(
                Id: SyncQuarantinedRemoteObjectId.FromIdentity(identity),
                ObjectKind: identity.Kind.ToString(),
                RelativePath: remoteInfo.RelativePath,
                RemoteEtag: remoteInfo.ETag,
                ContentHash: contentHash,
                ReasonCode: reasonCode,
                FirstSeenAtUtc: seenAtUtc,
                LastSeenAtUtc: seenAtUtc,
                SeenCount: 1));
    }

    private static bool IsSecretGenerationReset(
        string secretGenerationId,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        return SqliteSecretResetStore
            .LoadResetEvents(connection, transaction)
            .Any(resetEvent => string.Equals(
                resetEvent.SecretGenerationId,
                secretGenerationId,
                StringComparison.Ordinal));
    }

    private void DeferRemoteSecretItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var value = remoteItem.Value;
        var secretGenerationId = value.CryptoProfileSecretGenerationId;
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new InvalidOperationException("Remote secret item is missing crypto profile generation.");

        SqliteSyncMetadataStore.UpsertDeferredSecretItem(
            connection,
            transaction,
            new SyncDeferredSecretItemRecord(
                RemoteItemId: value.Id,
                SecretGenerationId: secretGenerationId,
                RemoteEtag: remoteItem.RemoteInfo.ETag,
                ContentHash: value.ContentHash,
                CanonicalJson: _serializer.Serialize(value),
                CreatedAtUtc: syncedAtUtc,
                LastAttemptAtUtc: null,
                AttemptCount: 0,
                LastErrorCode: null));
    }

    private int ApplyDeferredSecretItemsForProfile(
        CryptoProfileRecord profile,
        DateTimeOffset syncedAtUtc,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var appliedCount = 0;
        foreach (var deferredItem in SqliteSyncMetadataStore.LoadDeferredSecretItems(connection, transaction))
        {
            if (!string.Equals(deferredItem.SecretGenerationId, profile.SecretGenerationId, StringComparison.Ordinal))
                continue;

            var item = _serializer.Deserialize<SyncItemDto>(deferredItem.CanonicalJson.Span);
            ApplyRemoteSecretItem(
                new SyncAppliedRemoteObject<SyncItemDto>(
                    new SyncRemoteObjectInfo(
                        SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, item.Id)),
                        deferredItem.RemoteEtag,
                        LastModifiedUtc: null,
                        ContentLength: deferredItem.CanonicalJson.Length),
                    new SyncObjectIdentity(SyncObjectKind.Item, item.Id),
                    item),
                syncedAtUtc,
                profile,
                connection,
                transaction);
            SqliteSyncMetadataStore.DeleteDeferredSecretItem(connection, transaction, deferredItem.RemoteItemId);
            appliedCount++;
        }

        return appliedCount;
    }

    private static SyncObjectMetadata CreateCleanSyncMetadata(
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc,
        string modifiedDeviceId)
    {
        return new SyncObjectMetadata(
            BookmarkSyncState.Clean,
            remoteEtag,
            syncedAtUtc,
            contentHash,
            modifiedDeviceId);
    }

    private static BookmarkItemKind ToBookmarkItemKind(string kind)
    {
        return kind switch
        {
            SyncRemoteObjectConstants.FolderKind => BookmarkItemKind.Folder,
            SyncRemoteObjectConstants.BookmarkKind => BookmarkItemKind.Bookmark,
            _ => throw new InvalidOperationException("Remote item kind is not supported.")
        };
    }
}
