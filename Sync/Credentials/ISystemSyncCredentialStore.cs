namespace Stranichnik.Sync.Credentials;

public interface ISystemSyncCredentialStore
{
    SyncCredentials? Load(string username);

    bool Save(string username, SyncCredentials credentials);

    void Delete(string username);
}
