using System;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretBookmarkProjectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private const int TestPbkdf2Iterations = 1000;

    [Fact]
    public void Project_hides_secret_bookmarks_when_secrets_are_hidden()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out var profile);
        var secret = CreateSecretBookmark(crypto, session, profile.Id, "secret", "root");
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("root", null),
            secret,
            CreateBookmark("normal", "root")
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Contains(result.Snapshot.Items, item => item.Id == "normal");
        Assert.DoesNotContain(result.Snapshot.Items, item => item.Id == "secret");
        Assert.Contains(result.Snapshot.Items, item => item.Id == "root");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Project_hides_folder_that_contains_only_hidden_secret_bookmarks()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out var profile);
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("root", null),
            CreateFolder("secret-folder", "root"),
            CreateSecretBookmark(crypto, session, profile.Id, "secret", "secret-folder")
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Empty(result.Snapshot.Items);
    }

    [Fact]
    public void Project_keeps_genuinely_empty_folders_when_secrets_are_hidden()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out _);
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("empty", null)
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Contains(result.Snapshot.Items, item => item.Id == "empty");
    }

    [Fact]
    public void Project_keeps_folder_with_visible_child_when_secrets_are_hidden()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out var profile);
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("root", null),
            CreateSecretBookmark(crypto, session, profile.Id, "secret", "root"),
            CreateBookmark("normal", "root")
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Contains(result.Snapshot.Items, item => item.Id == "root");
        Assert.Contains(result.Snapshot.Items, item => item.Id == "normal");
        Assert.DoesNotContain(result.Snapshot.Items, item => item.Id == "secret");
    }

    [Fact]
    public void Project_hides_parent_folder_when_only_child_folder_becomes_hidden()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out var profile);
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("parent", null),
            CreateFolder("child", "parent"),
            CreateSecretBookmark(crypto, session, profile.Id, "secret", "child")
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Empty(result.Snapshot.Items);
    }

    [Fact]
    public void Project_decrypts_secret_bookmarks_when_secrets_are_visible()
    {
        using var session = CreateUnlockedSession(showSecrets: true, out var crypto, out var profile);
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateSecretBookmark(crypto, session, profile.Id, "secret", null)
        ]);
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);
        var projected = Assert.Single(result.Snapshot.Items);

        Assert.True(projected.IsSecret);
        Assert.Equal("Secret title", projected.Title);
        Assert.Equal("https://example.com/secret", projected.Url);
        Assert.NotNull(projected.EncryptedPayload);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Project_excludes_corrupt_secret_payload_without_throwing()
    {
        using var session = CreateUnlockedSession(showSecrets: true, out var crypto, out var profile);
        var encrypted = CreateEncryptedPayload(crypto, session, profile.Id, "secret");
        var corruptedPayload = encrypted.Payload.ToArray();
        corruptedPayload[^1] ^= 0x01;
        var corruptSecret = CreateSecretBookmark(
            "secret",
            null,
            new EncryptedBookmarkPayloadRecord(
                corruptedPayload,
                encrypted.Nonce,
                encrypted.CryptoProfileId,
                encrypted.PayloadFormatVersion));
        var service = new SecretBookmarkProjectionService(crypto);

        var snapshot = new BookmarkTreeSnapshot(new[] { corruptSecret });

        var result = service.Project(snapshot, session);

        Assert.Empty(result.Snapshot.Items);
        Assert.Contains(
            result.Warnings,
            warning => warning.Reason == SecretProjectionWarningReason.DecryptionFailed);
    }

    [Fact]
    public void Project_preserves_normal_records_order_and_parent_ids()
    {
        using var session = CreateUnlockedSession(showSecrets: false, out var crypto, out _);
        var folder = CreateFolder("folder", null);
        var first = CreateBookmark("first", "folder");
        var second = CreateBookmark("second", "folder");
        var snapshot = new BookmarkTreeSnapshot(new[] { folder, first, second });
        var service = new SecretBookmarkProjectionService(crypto);

        var result = service.Project(snapshot, session);

        Assert.Collection(
            result.Snapshot.Items.Select(item => item.Id),
            id => Assert.Equal("folder", id),
            id => Assert.Equal("first", id),
            id => Assert.Equal("second", id));
        Assert.Equal("folder", result.Snapshot.Items.Single(item => item.Id == "first").ParentId);
        Assert.Equal("folder", result.Snapshot.Items.Single(item => item.Id == "second").ParentId);
    }

    [Fact]
    public void Project_falls_back_to_hidden_projection_when_runtime_key_is_missing()
    {
        using var session = new SecretSessionService();
        session.MarkConfiguredLocked();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        byte[] payloadBytes = [1, 2, 3];
        byte[] nonceBytes = [4, 5, 6];
        var secret = CreateSecretBookmark(
            "secret",
            null,
            new EncryptedBookmarkPayloadRecord(payloadBytes, nonceBytes, 1));
        var service = new SecretBookmarkProjectionService(crypto);

        var snapshot = new BookmarkTreeSnapshot(new[] { secret });

        var result = service.Project(snapshot, session);

        Assert.Empty(result.Snapshot.Items);
        Assert.Empty(result.Warnings);
    }

    private static SecretSessionService CreateUnlockedSession(
        bool showSecrets,
        out SecretCryptoService crypto,
        out CryptoProfileRecord profile)
    {
        crypto = new SecretCryptoService(TestPbkdf2Iterations);
        var created = crypto.CreateProfile("password", Now);
        profile = created.Profile;
        var session = new SecretSessionService();
        session.ConfigureAndUnlock(created.DataKey, showSecrets);
        return session;
    }

    private static BookmarkItemRecord CreateSecretBookmark(
        SecretCryptoService crypto,
        ISecretSessionService session,
        long profileId,
        string id,
        string? parentId)
    {
        return CreateSecretBookmark(
            id,
            parentId,
            CreateEncryptedPayload(crypto, session, profileId, id));
    }

    private static EncryptedSecretPayload CreateEncryptedPayload(
        SecretCryptoService crypto,
        ISecretSessionService session,
        long profileId,
        string itemId)
    {
        var dataKey = session.BorrowDataKey() ?? throw new InvalidOperationException("Test session is locked.");
        return crypto.EncryptBookmarkPayload(
            new SecretBookmarkPayloadV1("Secret title", "https://example.com/secret"),
            dataKey,
            profileId,
            itemId);
    }

    private static BookmarkItemRecord CreateSecretBookmark(
        string id,
        string? parentId,
        EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        return new BookmarkItemRecord(
            id,
            parentId,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            Title: null,
            Url: null,
            IsSecret: true,
            encryptedPayload,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateSecretBookmark(
        string id,
        string? parentId,
        EncryptedSecretPayload encryptedPayload)
    {
        return CreateSecretBookmark(
            id,
            parentId,
            new EncryptedBookmarkPayloadRecord(
                encryptedPayload.Payload,
                encryptedPayload.Nonce,
                encryptedPayload.CryptoProfileId,
                encryptedPayload.PayloadFormatVersion));
    }

    private static BookmarkItemRecord CreateBookmark(string id, string? parentId)
    {
        return new BookmarkItemRecord(
            id,
            parentId,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            $"Title {id}",
            $"https://example.com/{id}",
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateFolder(string id, string? parentId)
    {
        return new BookmarkItemRecord(
            id,
            parentId,
            BookmarkItemKind.Folder,
            SortOrder: 1000,
            $"Folder {id}",
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemMetadata CreateMetadata()
    {
        return new BookmarkItemMetadata(
            Now,
            Now,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ModifiedDeviceId: "test");
    }
}
