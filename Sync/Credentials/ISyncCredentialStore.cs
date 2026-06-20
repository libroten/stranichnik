namespace Stranichnik.Sync.Credentials;

public interface ISyncCredentialStore
{
    SyncCredentials? Load();

    void SaveForSession(SyncCredentials credentials);

    SyncCredentialPersistResult SavePersistently(
        SyncCredentials credentials,
        string username,
        bool allowInsecureFallback);

    void ClearPersistent();

    void Clear();
}
