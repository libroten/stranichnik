using System;

namespace Stranichnik.Sync.Local;

public sealed record SyncQuarantinedRemoteObjectRecord(
    string Id,
    string ObjectKind,
    string RelativePath,
    string? RemoteEtag,
    string? ContentHash,
    string ReasonCode,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int SeenCount);
