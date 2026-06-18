using System.Collections.Generic;
using Stranichnik.Storage;

namespace Stranichnik.Sync.Local;

public sealed record SyncLocalSnapshot(
    SyncLocalIdentity Identity,
    IReadOnlyList<SyncItemSnapshotRecord> Items,
    IReadOnlyList<SyncIconAssetSnapshotRecord> IconAssets,
    IReadOnlyList<SyncSecretIconAssetSnapshotRecord> SecretIconAssets,
    IReadOnlyList<SyncCryptoProfileSnapshotRecord> CryptoProfiles,
    IReadOnlyList<SecretResetEventRecord> SecretResetEvents,
    IReadOnlyList<SyncPendingAssetRefRecord> PendingAssetRefs,
    IReadOnlyList<SyncDeferredSecretItemRecord> DeferredSecretItems,
    IReadOnlyList<SyncQuarantinedRemoteObjectRecord> QuarantinedRemoteObjects);
