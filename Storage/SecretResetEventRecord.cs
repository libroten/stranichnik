using System;

namespace Stranichnik.Storage;

public sealed record SecretResetEventRecord(
    string Id,
    string SecretGenerationId,
    DateTimeOffset ResetAtUtc,
    string ResetDeviceId,
    BookmarkSyncState SyncState,
    string? RemoteEtag,
    DateTimeOffset? LastSyncedAtUtc);

