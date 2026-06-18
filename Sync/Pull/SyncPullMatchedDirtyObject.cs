namespace Stranichnik.Sync.Pull;

public sealed record SyncPullMatchedDirtyObject(
    SyncObjectIdentity Identity,
    string? RemoteEtag,
    string ContentHash);
