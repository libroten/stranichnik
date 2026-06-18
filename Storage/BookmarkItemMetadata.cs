using System;

namespace Stranichnik.Storage;

public sealed record BookmarkItemMetadata(
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    int Revision,
    BookmarkSyncState SyncState,
    string? RemoteEtag,
    DateTimeOffset? LastSyncedAtUtc,
    string? ContentHash,
    string ModifiedDeviceId);
