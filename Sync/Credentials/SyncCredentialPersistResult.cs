namespace Stranichnik.Sync.Credentials;

public sealed record SyncCredentialPersistResult(
    SyncCredentialPersistStatus Status,
    SyncCredentialStorageKind StorageKind)
{
    public static SyncCredentialPersistResult Saved(SyncCredentialStorageKind storageKind)
    {
        return new SyncCredentialPersistResult(SyncCredentialPersistStatus.Saved, storageKind);
    }

    public static SyncCredentialPersistResult SystemStoreUnavailable()
    {
        return new SyncCredentialPersistResult(
            SyncCredentialPersistStatus.SystemStoreUnavailable,
            SyncCredentialStorageKind.SessionOnly);
    }

    public static SyncCredentialPersistResult InsecureFallbackFailed()
    {
        return new SyncCredentialPersistResult(
            SyncCredentialPersistStatus.InsecureFallbackFailed,
            SyncCredentialStorageKind.SessionOnly);
    }
}
