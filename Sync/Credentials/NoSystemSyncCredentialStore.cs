namespace Stranichnik.Sync.Credentials;

public sealed class NoSystemSyncCredentialStore : ISystemSyncCredentialStore
{
    public SyncCredentials? Load(string username)
    {
        return null;
    }

    public bool Save(string username, SyncCredentials credentials)
    {
        return false;
    }

    public void Delete(string username)
    {
    }
}
