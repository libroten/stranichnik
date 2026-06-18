using Stranichnik.Sync.Remote;

namespace Stranichnik.Sync;

public sealed record SyncRepositoryInitializationResult(
    SyncRepositoryInitializationStatus Status,
    SyncManifestDto? Manifest)
{
    public static SyncRepositoryInitializationResult Ready(SyncManifestDto manifest)
    {
        return new SyncRepositoryInitializationResult(SyncRepositoryInitializationStatus.Ready, manifest);
    }

    public static SyncRepositoryInitializationResult UnsupportedRepositoryVersion(SyncManifestDto manifest)
    {
        return new SyncRepositoryInitializationResult(SyncRepositoryInitializationStatus.UnsupportedRepositoryVersion, manifest);
    }

    public static SyncRepositoryInitializationResult InvalidRepository()
    {
        return new SyncRepositoryInitializationResult(SyncRepositoryInitializationStatus.InvalidRepository, null);
    }
}
