namespace Stranichnik.Sync.Pull;

public sealed record SyncPullQuarantineCandidate(
    string ObjectKind,
    string RelativePath,
    string? RemoteEtag,
    string? ContentHash,
    string ReasonCode);
