using System.Collections.Generic;

namespace Stranichnik.Sync.Local;

public interface ISyncMetadataStore
{
    SyncLocalIdentity LoadLocalIdentity();

    IReadOnlyList<SyncPendingAssetRefRecord> LoadPendingAssetRefs();

    void UpsertPendingAssetRef(SyncPendingAssetRefRecord record);

    void DeletePendingAssetRef(string id);

    void DeletePendingAssetRefForItem(string itemId, SyncPendingAssetKind assetKind);

    IReadOnlyList<SyncDeferredSecretItemRecord> LoadDeferredSecretItems();

    void UpsertDeferredSecretItem(SyncDeferredSecretItemRecord record);

    void DeleteDeferredSecretItem(string remoteItemId);

    void DeleteDeferredSecretItemsForGeneration(string secretGenerationId);

    IReadOnlyList<SyncQuarantinedRemoteObjectRecord> LoadQuarantinedRemoteObjects();

    void UpsertQuarantinedRemoteObject(SyncQuarantinedRemoteObjectRecord record);

    void DeleteQuarantinedRemoteObject(string id);
}
