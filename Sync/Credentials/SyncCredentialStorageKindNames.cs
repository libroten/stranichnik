using System;

namespace Stranichnik.Sync.Credentials;

public static class SyncCredentialStorageKindNames
{
    public const string SystemCredentialStore = "system";
    public const string ObfuscatedFile = "obfuscated-file";

    public static string ToSettingsValue(SyncCredentialStorageKind storageKind)
    {
        return storageKind switch
        {
            SyncCredentialStorageKind.SystemCredentialStore => SystemCredentialStore,
            SyncCredentialStorageKind.ObfuscatedFile => ObfuscatedFile,
            _ => string.Empty
        };
    }

    public static SyncCredentialStorageKind FromSettingsValue(string value)
    {
        return value switch
        {
            SystemCredentialStore => SyncCredentialStorageKind.SystemCredentialStore,
            ObfuscatedFile => SyncCredentialStorageKind.ObfuscatedFile,
            _ => SyncCredentialStorageKind.SessionOnly
        };
    }

    public static bool IsInsecureFile(string value)
    {
        return string.Equals(value, ObfuscatedFile, StringComparison.Ordinal);
    }
}
