using System;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretUnlockServiceTests
{
    [Fact]
    public void Unlock_with_correct_password_unlocks_visible_session()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var created = crypto.CreateProfile("password", Now);
        profileStore.SaveNewProfile(created.Profile);
        created.DataKey.Dispose();
        session.MarkConfiguredLocked();
        var service = new SecretUnlockService(profileStore, crypto, session);

        var result = service.Unlock("password", showSecrets: true);

        Assert.True(result.WasUnlocked);
        Assert.Null(result.FailureReason);
        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedVisible, session.Status);
        Assert.NotNull(session.BorrowDataKey());
    }

    [Fact]
    public void Unlock_with_correct_password_can_keep_secrets_hidden()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var created = crypto.CreateProfile("password", Now);
        profileStore.SaveNewProfile(created.Profile);
        created.DataKey.Dispose();
        session.MarkConfiguredLocked();
        var service = new SecretUnlockService(profileStore, crypto, session);

        var result = service.Unlock("password", showSecrets: false);

        Assert.True(result.WasUnlocked);
        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedHidden, session.Status);
        Assert.NotNull(session.BorrowDataKey());
    }

    [Fact]
    public void Unlock_with_wrong_password_keeps_session_locked()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var created = crypto.CreateProfile("password", Now);
        profileStore.SaveNewProfile(created.Profile);
        created.DataKey.Dispose();
        session.MarkConfiguredLocked();
        var service = new SecretUnlockService(profileStore, crypto, session);

        var result = service.Unlock("wrong-password", showSecrets: true);

        Assert.False(result.WasUnlocked);
        Assert.Equal(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile, result.FailureReason);
        Assert.Equal(SecretSessionStatus.ConfiguredLocked, session.Status);
        Assert.Null(session.BorrowDataKey());
    }

    [Fact]
    public void Unlock_without_profile_returns_not_configured()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var service = new SecretUnlockService(profileStore, crypto, session);

        var result = service.Unlock("password", showSecrets: true);

        Assert.False(result.WasUnlocked);
        Assert.Equal(SecretCryptoFailureReason.UnsupportedProfile, result.FailureReason);
        Assert.Equal(SecretSessionStatus.NotConfigured, session.Status);
    }

    private const int TestPbkdf2Iterations = 1000;
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
}
