using System;

namespace Stranichnik.Sync.Local;

public sealed record SyncPendingAssetRefRecord(
    string Id,
    string ItemId,
    SyncPendingAssetKind AssetKind,
    string RemoteAssetId,
    string? SourceHashAlgorithm,
    string? SourceHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    int AttemptCount,
    string? LastErrorCode);
