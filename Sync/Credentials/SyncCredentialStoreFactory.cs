using System;
using Stranichnik.Settings;

namespace Stranichnik.Sync.Credentials;

public static class SyncCredentialStoreFactory
{
    public static ISyncCredentialStore CreateDefault(bool simulateUnavailableSystemCredentialStore = false)
    {
        return new PersistentSyncCredentialStore(
            () => AppSettingsService.Load().Sync,
            simulateUnavailableSystemCredentialStore
                ? new NoSystemSyncCredentialStore()
                : CreateSystemStore(),
            new ObfuscatedFileSyncCredentialStore(AppDataPaths.SyncCredentialsPath));
    }

    private static ISystemSyncCredentialStore CreateSystemStore()
    {
        if (OperatingSystem.IsMacOS())
            return new MacOsKeychainSyncCredentialStore();

        if (OperatingSystem.IsWindows())
            return new WindowsCredentialManagerSyncCredentialStore();

        if (OperatingSystem.IsLinux())
            return new LinuxSecretToolSyncCredentialStore();

        return new NoSystemSyncCredentialStore();
    }
}
