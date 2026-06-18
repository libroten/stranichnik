using System;
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
}
