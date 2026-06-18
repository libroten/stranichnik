using System;

namespace Stranichnik.Sync.Local;

public sealed record SyncDeferredSecretItemRecord(
    string RemoteItemId,
    string SecretGenerationId,
    string? RemoteEtag,
    string ContentHash,
    ReadOnlyMemory<byte> CanonicalJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    int AttemptCount,
    string? LastErrorCode);
