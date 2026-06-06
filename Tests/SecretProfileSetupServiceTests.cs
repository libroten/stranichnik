using System;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretProfileSetupServiceTests
{
    [Fact]
    public void CreateMasterPassword_saves_profile_and_unlocks_visible_session()
    {
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var service = CreateService(profileStore, session);

        var result = service.CreateMasterPassword("password", showSecrets: true);

        Assert.True(result.WasCreated);
        Assert.NotNull(result.Profile);
        Assert.Same(result.Profile, profileStore.LoadActiveProfile());
        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedVisible, session.Status);
        Assert.NotNull(session.BorrowDataKey());
    }

    [Fact]
    public void CreateMasterPassword_can_unlock_hidden_session()
    {
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var service = CreateService(profileStore, session);

        var result = service.CreateMasterPassword("password", showSecrets: false);

        Assert.True(result.WasCreated);
        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedHidden, session.Status);
        Assert.NotNull(session.BorrowDataKey());
    }

    [Fact]
    public void CreateMasterPassword_does_not_replace_existing_profile()
    {
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var service = CreateService(profileStore, session);
        var created = service.CreateMasterPassword("password", showSecrets: true);

        var secondResult = service.CreateMasterPassword("other-password", showSecrets: false);

        Assert.False(secondResult.WasCreated);
        Assert.Null(secondResult.Profile);
        Assert.Same(created.Profile, profileStore.LoadActiveProfile());
        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedVisible, session.Status);
    }

    [Fact]
    public void CreateMasterPassword_rejects_empty_password()
    {
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var service = CreateService(profileStore, session);

        Assert.Throws<ArgumentException>(() => service.CreateMasterPassword(string.Empty, showSecrets: true));
        Assert.Null(profileStore.LoadActiveProfile());
        Assert.Equal(SecretSessionStatus.NotConfigured, session.Status);
    }

    private static SecretProfileSetupService CreateService(
        ISecretProfileStore profileStore,
        ISecretSessionService session)
    {
        return new(
            profileStore,
            new SecretCryptoService(TestPbkdf2Iterations),
            session,
            () => Now);
    }

    private const int TestPbkdf2Iterations = 1000;
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
}
