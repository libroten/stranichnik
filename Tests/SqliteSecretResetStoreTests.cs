using System;
using System.IO;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteSecretResetStoreTests
{
    [Fact]
    public void ResetMasterPasswordAndPurgeSecrets_creates_event_purges_secrets_and_deletes_profile()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.TreeStore.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("normal", isSecret: false),
            CreateBookmark("secret", isSecret: true),
        ]));
        database.TreeStore.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon"));
        database.TreeStore.SetItemSecretIconAsset("secret", "secret-icon");

        var result = database.ResetStore.ResetMasterPasswordAndPurgeSecrets("generation");

        Assert.Equal(1, result.PurgedSecretBookmarkCount);
        Assert.Null(database.ProfileStore.LoadActiveProfile());

        var loadedItems = database.TreeStore.Load().Items;
        Assert.Single(loadedItems);
        Assert.Equal("normal", loadedItems[0].Id);
        Assert.Equal(1, database.TreeStore.CountAllItems());
        Assert.Null(database.TreeStore.GetSecretIconAsset("secret-icon"));

        var resetEvent = Assert.Single(database.ResetStore.LoadResetEvents());
        Assert.Equal("reset-event", resetEvent.Id);
        Assert.Equal("generation", resetEvent.SecretGenerationId);
        Assert.Equal(Now, resetEvent.ResetAtUtc);
        Assert.Equal("test-device", resetEvent.ResetDeviceId);
        Assert.Equal(BookmarkSyncState.Dirty, resetEvent.SyncState);
    }

    [Fact]
    public void ResetMasterPasswordAndPurgeSecrets_rejects_wrong_generation()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));

        Assert.Throws<InvalidOperationException>(
            () => database.ResetStore.ResetMasterPasswordAndPurgeSecrets("other-generation"));
    }

    [Fact]
    public void ResetMasterPasswordAndPurgeSecrets_purges_folders_that_only_contained_secret_bookmarks()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.TreeStore.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("secret-folder", parentId: null),
            CreateFolder("secret-nested-folder", "secret-folder"),
            CreateBookmark("secret", "secret-nested-folder", isSecret: true),
        ]));

        var result = database.ResetStore.ResetMasterPasswordAndPurgeSecrets("generation");

        Assert.Equal(1, result.PurgedSecretBookmarkCount);
        Assert.Empty(database.TreeStore.Load().Items);
        Assert.Equal(0, database.TreeStore.CountAllItems());
    }

    [Fact]
    public void ResetMasterPasswordAndPurgeSecrets_keeps_folder_with_visible_non_secret_content()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.TreeStore.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("mixed-folder", parentId: null),
            CreateBookmark("secret", "mixed-folder", isSecret: true),
            CreateBookmark("normal", "mixed-folder", isSecret: false),
        ]));

        var result = database.ResetStore.ResetMasterPasswordAndPurgeSecrets("generation");

        Assert.Equal(1, result.PurgedSecretBookmarkCount);

        var loadedItems = database.TreeStore.Load().Items;
        Assert.Equal(2, loadedItems.Count);
        Assert.Contains(loadedItems, item => item.Id == "mixed-folder");
        Assert.Contains(loadedItems, item => item.Id == "normal");
        Assert.Equal(2, database.TreeStore.CountAllItems());
    }

    private static BookmarkItemRecord CreateBookmark(string id, bool isSecret)
    {
        return CreateBookmark(id, parentId: null, isSecret);
    }

    private static BookmarkItemRecord CreateBookmark(string id, string? parentId, bool isSecret)
    {
        return new(
            id,
            parentId,
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
                ContentHash: null,
                ModifiedDeviceId: "test-device"));
    }

    private static BookmarkItemRecord CreateFolder(string id, string? parentId)
    {
        return new(
            id,
            parentId,
            BookmarkItemKind.Folder,
            SortOrder: 1000,
            "Folder",
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                BookmarkSyncState.Dirty,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                ContentHash: null,
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

    private static SecretIconAssetRecord CreateSecretIconAsset(string id)
    {
        return new(
            id,
            "sha256",
            "source-hash",
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            new EncryptedSecretIconPayloadRecord(
                Payload: EncryptedPayload,
                Nonce: EncryptedNonce,
                PayloadFormatVersion: 1),
            "generation",
            Now);
    }

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
            new SqliteDatabaseMigrator(connectionFactory).Migrate();
            TreeStore = new SqliteBookmarkTreeStore(
                connectionFactory,
                clock: () => Now,
                modifiedDeviceId: "test-device");
            ProfileStore = new SqliteSecretProfileStore(connectionFactory);
            ResetStore = new SqliteSecretResetStore(
                connectionFactory,
                idFactory: () => "reset-event",
                clock: () => Now,
                resetDeviceId: "test-device");
        }

        public SqliteBookmarkTreeStore TreeStore { get; }

        public SqliteSecretProfileStore ProfileStore { get; }

        public SqliteSecretResetStore ResetStore { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                    Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 6, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] EncryptedPayload = [1, 2, 3];

    private static readonly byte[] EncryptedNonce = [4, 5, 6];

    private static readonly byte[] WrappedDataKey = [2, 3, 4];

    private static readonly byte[] WrappedDataKeyNonce = [5, 6, 7];

    private static readonly byte[] PasswordCheckPayload = [8, 9, 10];

    private static readonly byte[] PasswordCheckNonce = [11, 12, 13];
}
