using System;
using System.IO;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncCredentialStoreTests
{
    [Fact]
    public void InMemorySyncCredentialStore_saves_and_clears_session_credentials()
    {
        var store = new InMemorySyncCredentialStore();

        Assert.Null(store.Load());

        store.SaveForSession(new SyncCredentials("secret"));

        Assert.NotNull(store.Load());

        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void SyncCredentials_rejects_empty_password()
    {
        Assert.Throws<ArgumentException>(() => new SyncCredentials(string.Empty));
    }

    [Fact]
    public void ObfuscatedFileSyncCredentialStore_saves_password_without_plaintext()
    {
        var directory = Directory.CreateTempSubdirectory("stranichnik-credentials-test-");
        try
        {
            var filePath = Path.Combine(directory.FullName, "credentials.json");
            var store = new ObfuscatedFileSyncCredentialStore(filePath, _ => { });

            Assert.True(store.Save("user", new SyncCredentials("secret-password")));

            var fileText = File.ReadAllText(filePath);
            Assert.DoesNotContain("secret-password", fileText, StringComparison.Ordinal);

            var credentials = store.Load("user");
            Assert.NotNull(credentials);
            Assert.Equal("secret-password", credentials.Password);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ObfuscatedFileSyncCredentialStore_ignores_username_mismatch()
    {
        var directory = Directory.CreateTempSubdirectory("stranichnik-credentials-test-");
        try
        {
            var filePath = Path.Combine(directory.FullName, "credentials.json");
            var store = new ObfuscatedFileSyncCredentialStore(filePath, _ => { });

            Assert.True(store.Save("user", new SyncCredentials("secret-password")));

            Assert.Null(store.Load("another-user"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PersistentSyncCredentialStore_saves_to_system_store_when_available()
    {
        var directory = Directory.CreateTempSubdirectory("stranichnik-credentials-test-");
        try
        {
            var settings = new SyncSettings
            {
                Username = "user",
                CredentialStorageKind = SyncCredentialStorageKindNames.SystemCredentialStore
            };
            var systemStore = new FakeSystemSyncCredentialStore(canSave: true);
            var fileStore = new ObfuscatedFileSyncCredentialStore(
                Path.Combine(directory.FullName, "credentials.json"),
                _ => { });
            var store = new PersistentSyncCredentialStore(() => settings, systemStore, fileStore, _ => { });

            var result = store.SavePersistently(new SyncCredentials("secret-password"), "user", allowInsecureFallback: false);

            Assert.Equal(SyncCredentialPersistStatus.Saved, result.Status);
            Assert.Equal(SyncCredentialStorageKind.SystemCredentialStore, result.StorageKind);
            Assert.Equal("secret-password", systemStore.Load("user")?.Password);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PersistentSyncCredentialStore_uses_obfuscated_file_when_system_store_unavailable_and_fallback_allowed()
    {
        var directory = Directory.CreateTempSubdirectory("stranichnik-credentials-test-");
        try
        {
            var settings = new SyncSettings
            {
                Username = "user",
                CredentialStorageKind = SyncCredentialStorageKindNames.ObfuscatedFile
            };
            var systemStore = new FakeSystemSyncCredentialStore(canSave: false);
            var fileStore = new ObfuscatedFileSyncCredentialStore(
                Path.Combine(directory.FullName, "credentials.json"),
                _ => { });
            var store = new PersistentSyncCredentialStore(() => settings, systemStore, fileStore, _ => { });

            var result = store.SavePersistently(new SyncCredentials("secret-password"), "user", allowInsecureFallback: true);

            Assert.Equal(SyncCredentialPersistStatus.Saved, result.Status);
            Assert.Equal(SyncCredentialStorageKind.ObfuscatedFile, result.StorageKind);

            var reloadedStore = new PersistentSyncCredentialStore(() => settings, systemStore, fileStore, _ => { });
            Assert.Equal("secret-password", reloadedStore.Load()?.Password);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class FakeSystemSyncCredentialStore : ISystemSyncCredentialStore
    {
        private readonly bool _canSave;
        private SyncCredentials? _credentials;

        public FakeSystemSyncCredentialStore(bool canSave)
        {
            _canSave = canSave;
        }

        public SyncCredentials? Load(string username)
        {
            return _credentials;
        }

        public bool Save(string username, SyncCredentials credentials)
        {
            if (!_canSave)
                return false;

            _credentials = credentials;
            return true;
        }

        public void Delete(string username)
        {
            _credentials = null;
        }
    }
}
