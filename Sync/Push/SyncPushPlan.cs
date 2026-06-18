using System.Collections.Generic;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;

namespace Stranichnik.Sync.Push;

public sealed record SyncPushPlan(
    IReadOnlyList<SecretResetEventRecord> SecretResetEvents,
    IReadOnlyList<SyncCryptoProfileSnapshotRecord> CryptoProfiles,
    IReadOnlyList<SyncIconAssetSnapshotRecord> IconAssets,
    IReadOnlyList<SyncSecretIconAssetSnapshotRecord> SecretIconAssets,
    IReadOnlyList<SyncItemSnapshotRecord> Items);
