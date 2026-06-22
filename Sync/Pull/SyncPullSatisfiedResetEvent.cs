namespace Stranichnik.Sync.Pull;

public sealed record SyncPullSatisfiedResetEvent(
    string SecretGenerationId,
    string? RemoteEtag);
