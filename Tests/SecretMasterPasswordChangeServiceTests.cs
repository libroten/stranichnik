using System;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretMasterPasswordChangeServiceTests
{
    [Fact]
    public void ChangeMasterPassword_updates_profile_and_keeps_secret_payload_decryptable()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var created = crypto.CreateProfile("old password", Now);
        profileStore.SaveNewProfile(created.Profile);
        session.ConfigureAndUnlock(created.DataKey, showSecrets: false);
        var encrypted = crypto.EncryptBookmarkPayload(
            new SecretBookmarkPayloadV1("Secret Title", "https://secret.example.com"),
            Assert.IsType<RuntimeSecretKey>(session.BorrowDataKey()),
            created.Profile.Id,
            "secret");
        var service = CreateService(profileStore, crypto, session);

        var result = service.ChangeMasterPassword("new password");

        var updatedProfile = Assert.IsType<CryptoProfileRecord>(profileStore.LoadActiveProfile());
        var oldUnlock = crypto.Unlock(updatedProfile, "old password");
        var newUnlock = crypto.Unlock(updatedProfile, "new password");

        Assert.True(result.WasChanged);
        Assert.Null(result.FailureReason);
        Assert.False(oldUnlock.IsSuccess);
        Assert.True(newUnlock.IsSuccess);

        using var newDataKey = Assert.IsType<RuntimeSecretKey>(newUnlock.DataKey);
        var decrypted = crypto.DecryptBookmarkPayload(
            new EncryptedBookmarkPayloadRecord(
                encrypted.Payload,
                encrypted.Nonce,
                encrypted.CryptoProfileId,
                encrypted.PayloadFormatVersion),
            newDataKey,
            "secret");
        Assert.Equal("Secret Title", decrypted.Title);
        Assert.Equal("https://secret.example.com", decrypted.Url);
    }

    [Fact]
    public void ChangeMasterPassword_requires_existing_profile()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var service = CreateService(profileStore, crypto, session);

        var result = service.ChangeMasterPassword("new password");

        Assert.False(result.WasChanged);
        Assert.Equal(SecretMasterPasswordChangeFailureReason.NotConfigured, result.FailureReason);
    }

    [Fact]
    public void ChangeMasterPassword_requires_unlocked_session()
    {
        var profileStore = new InMemorySecretProfileStore();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        using var session = new SecretSessionService();
        var created = crypto.CreateProfile("old password", Now);
        profileStore.SaveNewProfile(created.Profile);
        created.DataKey.Dispose();
        session.MarkConfiguredLocked();
        var service = CreateService(profileStore, crypto, session);

        var result = service.ChangeMasterPassword("new password");

        Assert.False(result.WasChanged);
        Assert.Equal(SecretMasterPasswordChangeFailureReason.Locked, result.FailureReason);
    }

    private static SecretMasterPasswordChangeService CreateService(
        ISecretProfileStore profileStore,
        ISecretCryptoService crypto,
        ISecretSessionService session)
    {
        return new SecretMasterPasswordChangeService(
            profileStore,
            crypto,
            session,
            () => Now.AddMinutes(1));
    }

    private const int TestPbkdf2Iterations = 1000;
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
}
