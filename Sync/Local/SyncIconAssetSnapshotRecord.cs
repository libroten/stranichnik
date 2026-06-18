using Stranichnik.Storage;

namespace Stranichnik.Sync.Local;

public sealed record SyncIconAssetSnapshotRecord(
    BookmarkIconAssetRecord Asset,
    SyncObjectMetadata SyncMetadata);
