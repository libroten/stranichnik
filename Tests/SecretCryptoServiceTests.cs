using System;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretCryptoServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    private const int TestPbkdf2Iterations = 1000;

    [Fact]
    public void CreateProfile_unlocks_with_correct_password()
    {
        var service = CreateService();
        var created = service.CreateProfile("correct horse battery staple", Now);
        using var createdKey = created.DataKey;

        var unlock = service.Unlock(created.Profile, "correct horse battery staple");

        Assert.True(unlock.IsSuccess);
        Assert.Null(unlock.FailureReason);
        Assert.IsType<RuntimeSecretKey>(unlock.DataKey).Dispose();
    }

    [Fact]
    public void Unlock_rejects_wrong_password()
    {
        var service = CreateService();
        var created = service.CreateProfile("correct password", Now);
        using var createdKey = created.DataKey;

        var unlock = service.Unlock(created.Profile, "wrong password");

        Assert.False(unlock.IsSuccess);
        Assert.Equal(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile, unlock.FailureReason);
        Assert.Null(unlock.DataKey);
    }

    [Fact]
    public void EncryptBookmarkPayload_round_trips_bookmark_payload()
    {
        var service = CreateService();
        var created = service.CreateProfile("password", Now);
        using var dataKey = created.DataKey;
        var payload = new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret");

        var encrypted = service.EncryptBookmarkPayload(
            payload,
            dataKey,
            created.Profile.Id,
            "bookmark-1");

        var decrypted = service.DecryptBookmarkPayload(
            ToStoragePayload(encrypted),
            dataKey,
            "bookmark-1");

        Assert.Equal(payload, decrypted);
    }

    [Fact]
    public void EncryptBookmarkPayload_uses_fresh_nonce_for_same_payload()
    {
        var service = CreateService();
        var created = service.CreateProfile("password", Now);
        using var dataKey = created.DataKey;
        var payload = new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret");

        var first = service.EncryptBookmarkPayload(payload, dataKey, created.Profile.Id, "bookmark-1");
        var second = service.EncryptBookmarkPayload(payload, dataKey, created.Profile.Id, "bookmark-1");

        Assert.NotEqual(
            Convert.ToBase64String(first.Nonce.Span),
            Convert.ToBase64String(second.Nonce.Span));
        Assert.NotEqual(
            Convert.ToBase64String(first.Payload.Span),
            Convert.ToBase64String(second.Payload.Span));
    }

    [Fact]
    public void DecryptBookmarkPayload_rejects_wrong_item_id()
    {
        var service = CreateService();
        var created = service.CreateProfile("password", Now);
        using var dataKey = created.DataKey;
        var payload = new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret");
        var encrypted = service.EncryptBookmarkPayload(payload, dataKey, created.Profile.Id, "bookmark-1");

        var exception = Assert.Throws<SecretPayloadException>(
            () => service.DecryptBookmarkPayload(ToStoragePayload(encrypted), dataKey, "bookmark-2"));

        Assert.Equal(SecretCryptoFailureReason.InvalidPayload, exception.FailureReason);
    }

    [Fact]
    public void DecryptBookmarkPayload_rejects_corrupted_payload()
    {
        var service = CreateService();
        var created = service.CreateProfile("password", Now);
        using var dataKey = created.DataKey;
        var payload = new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret");
        var encrypted = service.EncryptBookmarkPayload(payload, dataKey, created.Profile.Id, "bookmark-1");
        var corruptedPayload = encrypted.Payload.ToArray();
        corruptedPayload[^1] ^= 0x01;

        var exception = Assert.Throws<SecretPayloadException>(
            () => service.DecryptBookmarkPayload(
                new EncryptedBookmarkPayloadRecord(
                    corruptedPayload,
                    encrypted.Nonce,
                    encrypted.CryptoProfileId),
                dataKey,
                "bookmark-1"));

        Assert.Equal(SecretCryptoFailureReason.InvalidPayload, exception.FailureReason);
    }

    [Fact]
    public void ChangeMasterPassword_rewraps_data_key_without_reencrypting_payload()
    {
        var service = CreateService();
        var created = service.CreateProfile("old password", Now);
        using var originalDataKey = created.DataKey;
        var payload = new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret");
        var encrypted = service.EncryptBookmarkPayload(
            payload,
            originalDataKey,
            created.Profile.Id,
            "bookmark-1");

        var updatedProfile = service.ChangeMasterPassword(
            created.Profile,
            originalDataKey,
            "new password",
            Now.AddMinutes(1));

        var oldUnlock = service.Unlock(updatedProfile, "old password");
        var newUnlock = service.Unlock(updatedProfile, "new password");

        Assert.False(oldUnlock.IsSuccess);
        Assert.True(newUnlock.IsSuccess);

        using var newDataKey = Assert.IsType<RuntimeSecretKey>(newUnlock.DataKey);
        var decrypted = service.DecryptBookmarkPayload(
            ToStoragePayload(encrypted),
            newDataKey,
            "bookmark-1");

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(
            Convert.ToBase64String(created.Profile.KdfSalt.Span),
            Convert.ToBase64String(updatedProfile.KdfSalt.Span));
        Assert.NotEqual(
            Convert.ToBase64String(created.Profile.WrappedDataKey.Span),
            Convert.ToBase64String(updatedProfile.WrappedDataKey.Span));
    }

    private static EncryptedBookmarkPayloadRecord ToStoragePayload(EncryptedSecretPayload payload)
    {
        return new EncryptedBookmarkPayloadRecord(
            payload.Payload,
            payload.Nonce,
            payload.CryptoProfileId,
            payload.PayloadFormatVersion);
    }

    private static SecretCryptoService CreateService()
    {
        return new SecretCryptoService(TestPbkdf2Iterations);
    }
}
