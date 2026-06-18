namespace Stranichnik.Sync.Pull;

public sealed record SyncPullConflict(
    SyncObjectIdentity Identity,
    string ReasonCode,
    string? LocalContentHash,
    string RemoteContentHash);
