using Stranichnik.Storage;

namespace Stranichnik.Sync.Local;

public sealed record SyncItemSnapshotRecord(
    BookmarkItemRecord Item,
    SyncObjectMetadata SyncMetadata);
