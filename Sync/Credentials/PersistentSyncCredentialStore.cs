using System;
using Stranichnik.Diagnostics;
using Stranichnik.Settings;

namespace Stranichnik.Sync.Credentials;

public sealed class PersistentSyncCredentialStore : ISyncCredentialStore
{
    private readonly InMemorySyncCredentialStore _sessionStore = new();
    private readonly Func<SyncSettings> _settingsProvider;
    private readonly ISystemSyncCredentialStore _systemStore;
    private readonly ObfuscatedFileSyncCredentialStore _fileStore;
    private readonly Action<string> _log;

    public PersistentSyncCredentialStore(
        Func<SyncSettings> settingsProvider,
        ISystemSyncCredentialStore systemStore,
        ObfuscatedFileSyncCredentialStore fileStore,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(systemStore);
        ArgumentNullException.ThrowIfNull(fileStore);

        _settingsProvider = settingsProvider;
        _systemStore = systemStore;
        _fileStore = fileStore;
        _log = log ?? Logs.Print;
    }

    public SyncCredentials? Load()
    {
        var sessionCredentials = _sessionStore.Load();
        if (sessionCredentials is not null)
            return sessionCredentials;

        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.CredentialStorageKind))
        {
            return null;
        }

        var storageKind = SyncCredentialStorageKindNames.FromSettingsValue(settings.CredentialStorageKind);
        var credentials = storageKind switch
        {
            SyncCredentialStorageKind.SystemCredentialStore => _systemStore.Load(settings.Username),
            SyncCredentialStorageKind.ObfuscatedFile => _fileStore.Load(settings.Username),
            _ => null
        };

        if (credentials is null)
            return null;

        _sessionStore.SaveForSession(credentials);
        return credentials;
    }

    public void SaveForSession(SyncCredentials credentials)
    {
        _sessionStore.SaveForSession(credentials);
    }

    public SyncCredentialPersistResult SavePersistently(
        SyncCredentials credentials,
        string username,
        bool allowInsecureFallback)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        _sessionStore.SaveForSession(credentials);

        if (_systemStore.Save(username, credentials))
        {
            _fileStore.Delete();
            _log("Sync credentials saved to system credential store.");
            return SyncCredentialPersistResult.Saved(SyncCredentialStorageKind.SystemCredentialStore);
        }

        _log("System credential store unavailable for sync credentials.");
        if (!allowInsecureFallback)
            return SyncCredentialPersistResult.SystemStoreUnavailable();

        if (_fileStore.Save(username, credentials))
            return SyncCredentialPersistResult.Saved(SyncCredentialStorageKind.ObfuscatedFile);

        return SyncCredentialPersistResult.InsecureFallbackFailed();
    }

    public void ClearPersistent()
    {
        var settings = _settingsProvider();
        if (!string.IsNullOrWhiteSpace(settings.Username))
            _systemStore.Delete(settings.Username);

        _fileStore.Delete();
    }

    public void Clear()
    {
        _sessionStore.Clear();
        ClearPersistent();
    }
}
