using System;

namespace Stranichnik.Sync;

public sealed record SyncRemoteObjectInfo(
    string RelativePath,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    long? ContentLength);
