using System;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretMasterPasswordResetServiceTests
{
    [Fact]
    public void ResetMasterPasswordAndDeleteSecrets_purges_secrets_and_marks_session_not_configured()
    {
        var treeStore = new InMemoryBookmarkTreeStore(
        [
            CreateBookmark("normal", isSecret: false),
            CreateBookmark("secret", isSecret: true),
        ]);
        var profileStore = new InMemorySecretProfileStore();
        var profile = CreateProfile("generation");
        profileStore.SaveNewProfile(profile);
        using var session = new SecretSessionService();
        session.ConfigureAndUnlock(new RuntimeSecretKey(new byte[32]), showSecrets: true);
        var resetStore = new InMemorySecretResetStore(
            treeStore,
            profileStore,
            idFactory: () => "reset-event",
            clock: () => Now,
            resetDeviceId: "test-device");
        var service = new SecretMasterPasswordResetService(profileStore, resetStore, session);

        var result = service.ResetMasterPasswordAndDeleteSecrets();

        Assert.True(result.WasReset);
        Assert.Equal(1, result.PurgedSecretBookmarkCount);
        Assert.Null(result.FailureReason);
        Assert.Null(profileStore.LoadActiveProfile());
        Assert.Equal(SecretSessionStatus.NotConfigured, session.Status);
        Assert.Single(treeStore.Load().Items);
        Assert.Equal("normal", treeStore.Load().Items[0].Id);

        var resetEvent = Assert.Single(resetStore.LoadResetEvents());
        Assert.Equal("reset-event", resetEvent.Id);
        Assert.Equal("generation", resetEvent.SecretGenerationId);
        Assert.Equal(Now, resetEvent.ResetAtUtc);
        Assert.Equal("test-device", resetEvent.ResetDeviceId);
        Assert.Equal(BookmarkSyncState.Dirty, resetEvent.SyncState);
    }

    [Fact]
    public void ResetMasterPasswordAndDeleteSecrets_returns_not_configured_without_profile()
    {
        var treeStore = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var resetStore = new InMemorySecretResetStore(treeStore, profileStore);
        var service = new SecretMasterPasswordResetService(profileStore, resetStore, session);

        var result = service.ResetMasterPasswordAndDeleteSecrets();

        Assert.False(result.WasReset);
        Assert.Equal(SecretMasterPasswordResetFailureReason.NotConfigured, result.FailureReason);
        Assert.Empty(resetStore.LoadResetEvents());
    }

    private static BookmarkItemRecord CreateBookmark(string id, bool isSecret)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            isSecret ? null : "Title",
            isSecret ? null : "https://example.com",
            isSecret,
            isSecret
                ? new EncryptedBookmarkPayloadRecord(EncryptedPayload, EncryptedNonce, 1, 1)
                : null,
            new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                BookmarkSyncState.Dirty,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                ModifiedDeviceId: "test-device"));
    }

    private static CryptoProfileRecord CreateProfile(string secretGenerationId)
    {
        return new(
            SecretCryptoProfileIds.ActiveProfileId,
            SecretEncryptionConstants.CurrentProfileVersion,
            SecretEncryptionConstants.KdfName,
            SecretEncryptionConstants.KdfHashAlgorithm,
            1000,
            Enumerable.Repeat((byte)1, SecretEncryptionConstants.KdfSaltLengthBytes).ToArray(),
            SecretEncryptionConstants.KekLengthBytes,
            SecretEncryptionConstants.DataKeyAlgorithm,
            WrappedDataKey,
            WrappedDataKeyNonce,
            SecretEncryptionConstants.EncryptionAlgorithm,
            SecretEncryptionConstants.PayloadFormat,
            PasswordCheckPayload,
            PasswordCheckNonce,
            Now,
            Now,
            secretGenerationId);
    }

    private static readonly DateTimeOffset Now = new(2026, 6, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] EncryptedPayload = [1, 2, 3];

    private static readonly byte[] EncryptedNonce = [4, 5, 6];

    private static readonly byte[] WrappedDataKey = [2, 3, 4];

    private static readonly byte[] WrappedDataKeyNonce = [5, 6, 7];

    private static readonly byte[] PasswordCheckPayload = [8, 9, 10];

    private static readonly byte[] PasswordCheckNonce = [11, 12, 13];
}
