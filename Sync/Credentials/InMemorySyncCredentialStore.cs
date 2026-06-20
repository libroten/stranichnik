using System;

namespace Stranichnik.Sync.Credentials;

public sealed class InMemorySyncCredentialStore : ISyncCredentialStore
{
    private readonly object _gate = new();
    private SyncCredentials? _credentials;

    public SyncCredentials? Load()
    {
        lock (_gate)
            return _credentials;
    }

    public void SaveForSession(SyncCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        lock (_gate)
            _credentials = credentials;
    }

    public SyncCredentialPersistResult SavePersistently(
        SyncCredentials credentials,
        string username,
        bool allowInsecureFallback)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        SaveForSession(credentials);
        return SyncCredentialPersistResult.Saved(SyncCredentialStorageKind.SessionOnly);
    }

    public void ClearPersistent()
    {
    }

    public void Clear()
    {
        lock (_gate)
            _credentials = null;
    }
}
