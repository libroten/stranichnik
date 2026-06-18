using Stranichnik.Storage;

namespace Stranichnik.Sync.Local;

public sealed record SyncSecretIconAssetSnapshotRecord(
    SecretIconAssetRecord Asset,
    SyncObjectMetadata SyncMetadata);
