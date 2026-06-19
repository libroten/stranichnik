using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSyncLocalStore : ISyncLocalStore
{
    private readonly SqliteBookmarkTreeStore _bookmarkTreeStore;
    private readonly SqliteSecretProfileStore _secretProfileStore;
    private readonly SqliteSecretResetStore _secretResetStore;
    private readonly ISyncMetadataStore _syncMetadataStore;
    private readonly ISyncJsonSerializer _serializer;

    public SqliteSyncLocalStore(
        SqliteBookmarkTreeStore bookmarkTreeStore,
        SqliteSecretProfileStore secretProfileStore,
        SqliteSecretResetStore secretResetStore,
        ISyncMetadataStore syncMetadataStore,
        ISyncJsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(bookmarkTreeStore);
        ArgumentNullException.ThrowIfNull(secretProfileStore);
        ArgumentNullException.ThrowIfNull(secretResetStore);
        ArgumentNullException.ThrowIfNull(syncMetadataStore);
        ArgumentNullException.ThrowIfNull(serializer);

        _bookmarkTreeStore = bookmarkTreeStore;
        _secretProfileStore = secretProfileStore;
        _secretResetStore = secretResetStore;
        _syncMetadataStore = syncMetadataStore;
        _serializer = serializer;
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

        foreach (var resetEvent in batch.SecretResetEvents)
            ApplyRemoteResetEvent(resetEvent, syncedAtUtc);

        foreach (var iconAsset in batch.IconAssets)
            ApplyRemoteIconAsset(iconAsset, syncedAtUtc);

        foreach (var secretIconAsset in batch.SecretIconAssets)
            ApplyRemoteSecretIconAsset(secretIconAsset, syncedAtUtc);

        foreach (var profile in batch.CryptoProfiles)
            ApplyRemoteCryptoProfile(profile, syncedAtUtc);

        foreach (var item in OrderItemsForApply(batch.Items))
            ApplyRemoteItem(item, syncedAtUtc);
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
    }

    public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        MarkSyncState(identity, BookmarkSyncState.Conflict);
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
    }

    public void ClearQuarantinedRemoteObject(string id)
    {
        _syncMetadataStore.DeleteQuarantinedRemoteObject(id);
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

    private void ApplyRemoteIconAsset(
        SyncAppliedRemoteObject<SyncIconAssetDto> remoteIconAsset,
        DateTimeOffset syncedAtUtc)
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

        _bookmarkTreeStore.UpsertRemoteIconAsset(
            iconAsset,
            CreateCleanSyncMetadata(
                remoteIconAsset.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ResolvePendingAssetRefs(SyncPendingAssetKind.RegularIcon, value.Id);
    }

    private void ApplyRemoteSecretIconAsset(
        SyncAppliedRemoteObject<SyncSecretIconAssetDto> remoteSecretIconAsset,
        DateTimeOffset syncedAtUtc)
    {
        var value = remoteSecretIconAsset.Value;
        if (IsSecretGenerationReset(value.SecretGenerationId))
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

        _bookmarkTreeStore.UpsertRemoteSecretIconAsset(
            iconAsset,
            CreateCleanSyncMetadata(
                remoteSecretIconAsset.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ResolvePendingAssetRefs(SyncPendingAssetKind.SecretIcon, value.Id);
    }

    private void ApplyRemoteResetEvent(
        SyncAppliedRemoteObject<SyncSecretResetEventDto> remoteResetEvent,
        DateTimeOffset syncedAtUtc)
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

        _secretResetStore.ApplyRemoteResetEventAndPurgeSecrets(resetEvent);
        _syncMetadataStore.DeleteDeferredSecretItemsForGeneration(value.SecretGenerationId);
    }

    private void ApplyRemoteCryptoProfile(
        SyncAppliedRemoteObject<SyncCryptoProfileDto> remoteProfile,
        DateTimeOffset syncedAtUtc)
    {
        var value = remoteProfile.Value;
        if (IsSecretGenerationReset(value.SecretGenerationId))
            return;

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

        _secretProfileStore.UpsertRemoteProfile(
            profile,
            CreateCleanSyncMetadata(
                remoteProfile.RemoteInfo.ETag,
                value.ContentHash,
                syncedAtUtc,
                value.ModifiedDeviceId));

        ApplyDeferredSecretItemsForProfile(profile, syncedAtUtc);
    }

    private void ApplyRemoteItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc)
    {
        var value = remoteItem.Value;
        if (HasMissingParent(value))
        {
            QuarantineRemoteObject(
                remoteItem.RemoteInfo,
                remoteItem.Identity,
                value.ContentHash,
                reasonCode: "missing-parent",
                syncedAtUtc);
            return;
        }

        if (value.IsSecret)
        {
            ApplyOrDeferRemoteSecretItem(remoteItem, syncedAtUtc);
            return;
        }

        var iconAssetId = ResolveRegularIconAssetId(value);
        var item = new BookmarkItemRecord(
            value.Id,
            value.ParentId,
            ToBookmarkItemKind(value.Kind),
            value.SortOrder,
            value.Title,
            value.Url,
            IsSecret: false,
            EncryptedPayload: null,
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

        _bookmarkTreeStore.UpsertRemoteItem(item);
        UpdatePendingRegularIconRef(value, syncedAtUtc, iconAssetId);
    }

    private List<SyncAppliedRemoteObject<SyncItemDto>> OrderItemsForApply(
        IReadOnlyList<SyncAppliedRemoteObject<SyncItemDto>> items)
    {
        var remaining = items.ToList();
        var ordered = new List<SyncAppliedRemoteObject<SyncItemDto>>(remaining.Count);
        var availableIds = _bookmarkTreeStore
            .LoadAllItemsForSync()
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

    private bool HasMissingParent(SyncItemDto item)
    {
        if (item.ParentId is null)
            return false;

        return item.DeletedAtUtc is null
            ? !_bookmarkTreeStore.ItemExistsForSync(item.ParentId)
            : !_bookmarkTreeStore.ItemExistsIncludingDeletedForSync(item.ParentId);
    }

    private string? ResolveRegularIconAssetId(SyncItemDto item)
    {
        if (item.DeletedAtUtc is not null || item.IconAssetRef is null)
            return null;

        return _bookmarkTreeStore.GetIconAsset(item.IconAssetRef.AssetId) is null
            ? null
            : item.IconAssetRef.AssetId;
    }

    private void UpdatePendingRegularIconRef(
        SyncItemDto item,
        DateTimeOffset syncedAtUtc,
        string? resolvedIconAssetId)
    {
        if (item.DeletedAtUtc is not null || item.IconAssetRef is null || resolvedIconAssetId is not null)
        {
            _syncMetadataStore.DeletePendingAssetRefForItem(item.Id, SyncPendingAssetKind.RegularIcon);
            return;
        }

        _syncMetadataStore.UpsertPendingAssetRef(new SyncPendingAssetRefRecord(
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
        DateTimeOffset syncedAtUtc)
    {
        var value = remoteItem.Value;
        if (string.IsNullOrWhiteSpace(value.CryptoProfileSecretGenerationId))
            throw new InvalidOperationException("Remote secret item is missing crypto profile generation.");

        if (IsSecretGenerationReset(value.CryptoProfileSecretGenerationId))
        {
            _syncMetadataStore.DeleteDeferredSecretItem(value.Id);
            return;
        }

        var profile = _secretProfileStore.LoadActiveProfile();
        if (profile is not null &&
            string.Equals(
                profile.SecretGenerationId,
                value.CryptoProfileSecretGenerationId,
                StringComparison.Ordinal))
        {
            ApplyRemoteSecretItem(remoteItem, syncedAtUtc, profile);
            _syncMetadataStore.DeleteDeferredSecretItem(value.Id);
            return;
        }

        DeferRemoteSecretItem(remoteItem, syncedAtUtc);
    }

    private void ApplyRemoteSecretItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc,
        CryptoProfileRecord profile)
    {
        var value = remoteItem.Value;
        if (string.IsNullOrWhiteSpace(value.EncryptedPayload) ||
            string.IsNullOrWhiteSpace(value.EncryptionNonce) ||
            value.SecretPayloadFormatVersion is null)
        {
            throw new InvalidOperationException("Remote secret item encrypted payload is incomplete.");
        }

        if (IsSecretGenerationReset(profile.SecretGenerationId))
            return;

        var secretIconAssetId = ResolveSecretIconAssetId(value);
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

        _bookmarkTreeStore.UpsertRemoteItem(item);
        UpdatePendingSecretIconRef(value, syncedAtUtc, secretIconAssetId);
    }

    private string? ResolveSecretIconAssetId(SyncItemDto item)
    {
        if (item.DeletedAtUtc is not null || item.SecretIconAssetRef is null)
            return null;

        return _bookmarkTreeStore.GetSecretIconAsset(item.SecretIconAssetRef.AssetId) is null
            ? null
            : item.SecretIconAssetRef.AssetId;
    }

    private void UpdatePendingSecretIconRef(
        SyncItemDto item,
        DateTimeOffset syncedAtUtc,
        string? resolvedSecretIconAssetId)
    {
        if (item.DeletedAtUtc is not null || item.SecretIconAssetRef is null || resolvedSecretIconAssetId is not null)
        {
            _syncMetadataStore.DeletePendingAssetRefForItem(item.Id, SyncPendingAssetKind.SecretIcon);
            return;
        }

        _syncMetadataStore.UpsertPendingAssetRef(new SyncPendingAssetRefRecord(
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

    private void ResolvePendingAssetRefs(
        SyncPendingAssetKind assetKind,
        string remoteAssetId)
    {
        foreach (var pendingRef in _syncMetadataStore.LoadPendingAssetRefs())
        {
            if (pendingRef.AssetKind != assetKind ||
                !string.Equals(pendingRef.RemoteAssetId, remoteAssetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_bookmarkTreeStore.TrySetRemoteResolvedIconAsset(
                    pendingRef.ItemId,
                    assetKind,
                    remoteAssetId))
            {
                _syncMetadataStore.DeletePendingAssetRef(pendingRef.Id);
            }
        }
    }

    private void QuarantineRemoteObject(
        SyncRemoteObjectInfo remoteInfo,
        SyncObjectIdentity identity,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc)
    {
        _syncMetadataStore.UpsertQuarantinedRemoteObject(new SyncQuarantinedRemoteObjectRecord(
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

    private bool IsSecretGenerationReset(string secretGenerationId)
    {
        return _secretResetStore
            .LoadResetEvents()
            .Any(resetEvent => string.Equals(
                resetEvent.SecretGenerationId,
                secretGenerationId,
                StringComparison.Ordinal));
    }

    private void DeferRemoteSecretItem(
        SyncAppliedRemoteObject<SyncItemDto> remoteItem,
        DateTimeOffset syncedAtUtc)
    {
        var value = remoteItem.Value;
        var secretGenerationId = value.CryptoProfileSecretGenerationId;
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new InvalidOperationException("Remote secret item is missing crypto profile generation.");

        _syncMetadataStore.UpsertDeferredSecretItem(new SyncDeferredSecretItemRecord(
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

    private void ApplyDeferredSecretItemsForProfile(
        CryptoProfileRecord profile,
        DateTimeOffset syncedAtUtc)
    {
        foreach (var deferredItem in _syncMetadataStore.LoadDeferredSecretItems())
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
                profile);
            _syncMetadataStore.DeleteDeferredSecretItem(deferredItem.RemoteItemId);
        }
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
