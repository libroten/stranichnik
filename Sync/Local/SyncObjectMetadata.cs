using System;
using Stranichnik.Storage;

namespace Stranichnik.Sync.Local;

public sealed record SyncObjectMetadata(
    BookmarkSyncState SyncState,
    string? RemoteEtag,
    DateTimeOffset? LastSyncedAtUtc,
    string? ContentHash,
    string ModifiedDeviceId);
