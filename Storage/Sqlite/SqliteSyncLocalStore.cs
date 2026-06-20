using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly SqliteBookmarkTreeStore _bookmarkTreeStore;
    private readonly SqliteSecretProfileStore _secretProfileStore;
    private readonly SqliteSecretResetStore _secretResetStore;
    private readonly ISyncMetadataStore _syncMetadataStore;
    private readonly ISyncJsonSerializer _serializer;
    private readonly Action<string> _log;

    public SqliteSyncLocalStore(
        SqliteBookmarkTreeStore bookmarkTreeStore,
        SqliteSecretProfileStore secretProfileStore,
        SqliteSecretResetStore secretResetStore,
        ISyncMetadataStore syncMetadataStore,
        ISyncJsonSerializer serializer,
        Action<string>? log = null)
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
            ApplyRemoteResetEvent(resetEvent, syncedAtUtc);
            _log("SQLite sync apply remote object finished. Kind=SecretResetEvent.");
        }

        foreach (var iconAsset in batch.IconAssets)
        {
            _log("SQLite sync apply remote object started. Kind=IconAsset.");
            ApplyRemoteIconAsset(iconAsset, syncedAtUtc);
            _log("SQLite sync apply remote object finished. Kind=IconAsset.");
        }

        foreach (var secretIconAsset in batch.SecretIconAssets)
        {
            _log("SQLite sync apply remote object started. Kind=SecretIconAsset.");
            ApplyRemoteSecretIconAsset(secretIconAsset, syncedAtUtc);
            _log("SQLite sync apply remote object finished. Kind=SecretIconAsset.");
        }

        foreach (var profile in batch.CryptoProfiles)
        {
            _log("SQLite sync apply remote object started. Kind=CryptoProfile.");
            ApplyRemoteCryptoProfile(profile, syncedAtUtc);
            _log("SQLite sync apply remote object finished. Kind=CryptoProfile.");
        }

        foreach (var item in OrderItemsForApply(batch.Items))
        {
            _log("SQLite sync apply remote object started. Kind=Item.");
            ApplyRemoteItem(item, syncedAtUtc);
            _log("SQLite sync apply remote object finished. Kind=Item.");
        }

        ReconcilePendingRemoteDependencies(syncedAtUtc);
        _log("SQLite sync apply remote changes finished.");
    }

    public void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _log("SQLite sync apply pull plan started.");
        ApplyRemoteChanges(plan.ApplyBatch);

        foreach (var matchedObject in plan.MatchedDirtyObjects)
        {
            MarkUploaded(
                matchedObject.Identity,
                matchedObject.RemoteEtag,
                matchedObject.ContentHash,
                syncedAtUtc);
        }

        foreach (var conflict in plan.Conflicts)
            MarkConflict(conflict.Identity, conflict.ReasonCode);

        foreach (var quarantineCandidate in plan.QuarantinedRemoteObjects)
        {
            MarkQuarantinedRemoteObject(
                quarantineCandidate.ObjectKind,
                quarantineCandidate.RelativePath,
                quarantineCandidate.RemoteEtag,
                quarantineCandidate.ContentHash,
                quarantineCandidate.ReasonCode,
                syncedAtUtc);
        }

        foreach (var quarantineCandidate in plan.KnownQuarantinedRemoteObjects)
        {
            MarkQuarantinedRemoteObject(
                quarantineCandidate.ObjectKind,
                quarantineCandidate.RelativePath,
                quarantineCandidate.RemoteEtag,
                quarantineCandidate.ContentHash,
                quarantineCandidate.ReasonCode,
                syncedAtUtc);
        }

        foreach (var resolvedQuarantineId in plan.ResolvedQuarantinedRemoteObjectIds.Distinct(StringComparer.Ordinal))
            ClearQuarantinedRemoteObject(resolvedQuarantineId);

        foreach (var missingRemoteObject in plan.MissingRemoteObjects)
            MarkRemoteMissingAsDirty(missingRemoteObject);
        _log("SQLite sync apply pull plan finished.");
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

    public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        MarkSyncState(identity, BookmarkSyncState.Conflict);
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

    public void ClearQuarantinedRemoteObject(string id)
    {
        _syncMetadataStore.DeleteQuarantinedRemoteObject(id);
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

    private void ReconcilePendingRemoteDependencies(DateTimeOffset syncedAtUtc)
    {
        var resolvedAssetRefs = 0;
        foreach (var pendingRef in _syncMetadataStore.LoadPendingAssetRefs())
        {
            if (ResolvePendingAssetRef(pendingRef))
                resolvedAssetRefs++;
        }

        var appliedDeferredSecretItems = 0;
        var profile = _secretProfileStore.LoadActiveProfile();
        if (profile is not null && !IsSecretGenerationReset(profile.SecretGenerationId))
            appliedDeferredSecretItems = ApplyDeferredSecretItemsForProfile(profile, syncedAtUtc);

        _log(
            "SQLite sync pending dependency reconciliation finished. " +
            $"ResolvedAssetRefs={resolvedAssetRefs}; " +
            $"AppliedDeferredSecretItems={appliedDeferredSecretItems}.");
    }

    private int ResolvePendingAssetRefs(
        SyncPendingAssetKind assetKind,
        string remoteAssetId)
    {
        var resolvedCount = 0;
        foreach (var pendingRef in _syncMetadataStore.LoadPendingAssetRefs())
        {
            if (pendingRef.AssetKind != assetKind ||
                !string.Equals(pendingRef.RemoteAssetId, remoteAssetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (ResolvePendingAssetRef(pendingRef))
                resolvedCount++;
        }

        return resolvedCount;
    }

    private bool ResolvePendingAssetRef(SyncPendingAssetRefRecord pendingRef)
    {
        if (!RemoteAssetExists(pendingRef.AssetKind, pendingRef.RemoteAssetId))
            return false;

        if (!_bookmarkTreeStore.TrySetRemoteResolvedIconAsset(
                pendingRef.ItemId,
                pendingRef.AssetKind,
                pendingRef.RemoteAssetId))
        {
            return false;
        }

        _syncMetadataStore.DeletePendingAssetRef(pendingRef.Id);
        return true;
    }

    private bool RemoteAssetExists(SyncPendingAssetKind assetKind, string remoteAssetId)
    {
        return assetKind switch
        {
            SyncPendingAssetKind.RegularIcon => _bookmarkTreeStore.GetIconAsset(remoteAssetId) is not null,
            SyncPendingAssetKind.SecretIcon => _bookmarkTreeStore.GetSecretIconAsset(remoteAssetId) is not null,
            _ => false
        };
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

    private int ApplyDeferredSecretItemsForProfile(
        CryptoProfileRecord profile,
        DateTimeOffset syncedAtUtc)
    {
        var appliedCount = 0;
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
