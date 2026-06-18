namespace Stranichnik.Sync.Credentials;

public interface ISyncCredentialStore
{
    SyncCredentials? Load();

    void SaveForSession(SyncCredentials credentials);

    void Clear();
}
