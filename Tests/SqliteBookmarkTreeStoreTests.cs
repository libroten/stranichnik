using System;
using System.IO;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteBookmarkTreeStoreTests
{
    [Fact]
    public void Load_reads_visible_rows_in_storage_order()
    {
        using var database = TempSqliteDatabase.Create();
        var existing = CreateBookmark("existing", "folder", 1000);
        var newest = CreateBookmark("newest", "folder", 2000);
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            existing,
            newest,
        ]));

        var loaded = database.Store.Load().Items
            .Where(item => item.ParentId == "folder")
            .ToList();

        Assert.Collection(
            loaded,
            item => Assert.Equal("newest", item.Id),
            item => Assert.Equal("existing", item.Id));
    }

    [Fact]
    public void Load_hides_tombstoned_rows()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("visible", parentId: null, sortOrder: 2000),
            CreateBookmark("deleted", parentId: null, sortOrder: 1000) with
            {
                Metadata = CreateMetadata() with
                {
                    DeletedAtUtc = UpdatedAt
                }
            },
        ]));

        var loaded = database.Store.Load().Items;

        Assert.Single(loaded);
        Assert.Equal("visible", loaded[0].Id);
    }

    [Fact]
    public void AddBookmarkToFolderStart_adds_bookmark_to_folder_start()
    {
        using var database = TempSqliteDatabase.Create();
        var existingBookmark = CreateBookmark("existing", "folder", 1000);
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            existingBookmark,
        ]));

        var bookmark = database.Store.AddBookmarkToFolderStart("folder", " Docs ", " https://docs.example.com ");

        var folderBookmarks = database.Store.Load().Items
            .Where(item => item.ParentId == "folder")
            .ToList();

        Assert.Equal("Docs", bookmark.Title);
        Assert.Equal("https://docs.example.com", bookmark.Url);
        Assert.Equal(BookmarkItemKind.Bookmark, bookmark.Kind);
        Assert.Equal("folder", bookmark.ParentId);
        Assert.Equal(2000, bookmark.SortOrder);
        Assert.Equal("created-1", bookmark.Id);
        Assert.Collection(
            folderBookmarks,
            item => Assert.Equal("created-1", item.Id),
            item => Assert.Equal("existing", item.Id));
    }

    [Fact]
    public void AddFolderToFolderStart_adds_folder_to_root_start()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("existing", parentId: null, sortOrder: 1000),
        ]));

        var folder = database.Store.AddFolderToFolderStart(parentId: null, " New Folder ");

        var rootItems = database.Store.Load().Items
            .Where(item => item.ParentId is null)
            .ToList();

        Assert.Equal("New Folder", folder.Title);
        Assert.Equal(BookmarkItemKind.Folder, folder.Kind);
        Assert.Null(folder.ParentId);
        Assert.Equal(2000, folder.SortOrder);
        Assert.Equal("created-1", folder.Id);
        Assert.Collection(
            rootItems,
            item => Assert.Equal("created-1", item.Id),
            item => Assert.Equal("existing", item.Id));
    }

    [Fact]
    public void AddBookmarkToFolderStart_rejects_non_folder_parent()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.AddBookmarkToFolderStart("bookmark", "Title", "https://example.com"));
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_adds_secret_bookmark_without_plaintext()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
        ]));
        var payload = CreateEncryptedPayload();

        var bookmark = database.Store.AddSecretBookmarkToFolderStart("folder", "created-1", payload);

        Assert.True(bookmark.IsSecret);
        Assert.Null(bookmark.Title);
        Assert.Null(bookmark.Url);
        Assert.Null(bookmark.IconAssetId);
        AssertEncryptedPayloadEqual(payload, Assert.IsType<EncryptedBookmarkPayloadRecord>(bookmark.EncryptedPayload));

        var loaded = database.Store.Load().Items.Single(item => item.Id == "created-1");
        Assert.True(loaded.IsSecret);
        Assert.Null(loaded.Title);
        Assert.Null(loaded.Url);
        AssertEncryptedPayloadEqual(payload, Assert.IsType<EncryptedBookmarkPayloadRecord>(loaded.EncryptedPayload));
    }

    [Fact]
    public void AddBookmarkToFolderStart_rejects_empty_title_or_url()
    {
        using var database = TempSqliteDatabase.Create();

        Assert.Throws<ArgumentException>(
            () => database.Store.AddBookmarkToFolderStart(parentId: null, " ", "https://example.com"));
        Assert.Throws<ArgumentException>(
            () => database.Store.AddBookmarkToFolderStart(parentId: null, "Title", " "));
    }

    [Fact]
    public void AddFolderToFolderStart_rejects_empty_title()
    {
        using var database = TempSqliteDatabase.Create();

        Assert.Throws<ArgumentException>(
            () => database.Store.AddFolderToFolderStart(parentId: null, " "));
    }

    [Fact]
    public void EditBookmark_updates_title_url_and_metadata()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        var edited = database.Store.EditBookmark("bookmark", " New ", " https://new.example.com ");

        Assert.Equal("New", edited.Title);
        Assert.Equal("https://new.example.com", edited.Url);
        Assert.Equal(2, edited.Metadata.Revision);
        Assert.Equal(UpdatedAt, edited.Metadata.UpdatedAtUtc);
        Assert.Equal(BookmarkSyncState.Dirty, edited.Metadata.SyncState);
    }

    [Fact]
    public void EditBookmarkAsSecret_clears_plaintext_and_icon()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));
        database.Store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));
        database.Store.SetItemIconAsset("bookmark", "icon");
        var payload = CreateEncryptedPayload();

        var edited = database.Store.EditBookmarkAsSecret("bookmark", payload);

        Assert.True(edited.IsSecret);
        Assert.Null(edited.Title);
        Assert.Null(edited.Url);
        Assert.Null(edited.IconAssetId);
        AssertEncryptedPayloadEqual(payload, Assert.IsType<EncryptedBookmarkPayloadRecord>(edited.EncryptedPayload));
        Assert.Equal(3, edited.Metadata.Revision);

        var loaded = database.Store.Load().Items.Single();
        Assert.True(loaded.IsSecret);
        Assert.Null(loaded.Title);
        Assert.Null(loaded.Url);
        Assert.Null(loaded.IconAssetId);
    }

    [Fact]
    public void EditSecretBookmarkAsPlaintext_restores_plaintext_and_clears_secret_payload()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        var edited = database.Store.EditSecretBookmarkAsPlaintext(
            "bookmark",
            " Title ",
            " https://example.com ");

        Assert.False(edited.IsSecret);
        Assert.Equal("Title", edited.Title);
        Assert.Equal("https://example.com", edited.Url);
        Assert.Null(edited.EncryptedPayload);
        Assert.Null(edited.IconAssetId);

        var loaded = database.Store.Load().Items.Single();
        Assert.False(loaded.IsSecret);
        Assert.Equal("Title", loaded.Title);
        Assert.Equal("https://example.com", loaded.Url);
        Assert.Null(loaded.EncryptedPayload);
        Assert.Null(loaded.IconAssetId);
    }

    [Fact]
    public void EditSecretBookmarkAsPlaintext_rejects_normal_bookmark()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditSecretBookmarkAsPlaintext(
                "bookmark",
                "Title",
                "https://example.com"));
    }

    [Fact]
    public void EditBookmarkAsSecret_rejects_folder()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditBookmarkAsSecret("folder", CreateEncryptedPayload()));
    }

    [Fact]
    public void EditBookmark_rejects_wrong_type_and_tombstoned_item()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateBookmark("bookmark", parentId: null, sortOrder: 500),
        ]));

        database.Store.DeleteItem("bookmark");

        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditBookmark("folder", "Title", "https://example.com"));
        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditBookmark("bookmark", "Title", "https://example.com"));
    }

    [Fact]
    public void EditFolder_updates_title_and_metadata()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
        ]));

        var edited = database.Store.EditFolder("folder", " New Folder ");

        Assert.Equal("New Folder", edited.Title);
        Assert.Equal(2, edited.Metadata.Revision);
        Assert.Equal(UpdatedAt, edited.Metadata.UpdatedAtUtc);
        Assert.Equal(BookmarkSyncState.Dirty, edited.Metadata.SyncState);
    }

    [Fact]
    public void GetOrCreateIconAsset_inserts_and_loads_asset()
    {
        using var database = TempSqliteDatabase.Create();
        var iconAsset = CreateIconAsset("icon", "hash");

        var created = database.Store.GetOrCreateIconAsset(iconAsset);
        var loaded = database.Store.GetIconAsset("icon");

        AssertIconAssetEqual(iconAsset, created);
        AssertIconAssetEqual(iconAsset, Assert.IsType<BookmarkIconAssetRecord>(loaded));
    }

    [Fact]
    public void GetOrCreateIconAsset_reuses_existing_asset_with_same_source_hash()
    {
        using var database = TempSqliteDatabase.Create();
        var first = CreateIconAsset("icon-1", "same-hash");
        var second = CreateIconAsset("icon-2", "same-hash");

        var created = database.Store.GetOrCreateIconAsset(first);
        var reused = database.Store.GetOrCreateIconAsset(second);

        AssertIconAssetEqual(first, created);
        AssertIconAssetEqual(first, reused);
        AssertIconAssetEqual(
            first,
            Assert.IsType<BookmarkIconAssetRecord>(database.Store.GetIconAsset("icon-1")));
        AssertIconAssetEqual(
            first,
            Assert.IsType<BookmarkIconAssetRecord>(database.Store.GetIconAssetBySourceHash("sha256", "same-hash")));
        Assert.Null(database.Store.GetIconAsset("icon-2"));
    }

    [Fact]
    public void SetItemIconAsset_updates_and_clears_icon_reference()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));
        database.Store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));

        var withIcon = database.Store.SetItemIconAsset("bookmark", "icon");
        var withoutIcon = database.Store.SetItemIconAsset("bookmark", iconAssetId: null);

        Assert.Equal("icon", withIcon.IconAssetId);
        Assert.Null(withoutIcon.IconAssetId);
        Assert.Equal(3, withoutIcon.Metadata.Revision);
        Assert.Null(database.Store.Load().Items.Single().IconAssetId);
    }

    [Fact]
    public void SetItemIconAsset_rejects_missing_icon_asset()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.SetItemIconAsset("bookmark", "missing"));
    }

    [Fact]
    public void SetItemIconAsset_rejects_secret_bookmark_custom_icon()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));
        database.Store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.SetItemIconAsset("bookmark", "icon"));
    }

    [Fact]
    public void DeleteItem_does_not_delete_shared_icon_asset()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));
        var iconAsset = CreateIconAsset("icon", "hash");
        database.Store.GetOrCreateIconAsset(iconAsset);
        database.Store.SetItemIconAsset("bookmark", "icon");

        database.Store.DeleteItem("bookmark");

        AssertIconAssetEqual(
            iconAsset,
            Assert.IsType<BookmarkIconAssetRecord>(database.Store.GetIconAsset("icon")));
    }

    [Fact]
    public void EditFolder_rejects_wrong_type_and_tombstoned_item()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateBookmark("bookmark", parentId: null, sortOrder: 500),
        ]));

        database.Store.DeleteItem("folder");

        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditFolder("bookmark", "Folder"));
        Assert.Throws<InvalidOperationException>(
            () => database.Store.EditFolder("folder", "Folder"));
    }

    [Fact]
    public void DeleteItem_tombstones_bookmark_and_hides_it_from_load()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        database.Store.DeleteItem("bookmark");

        Assert.Empty(database.Store.Load().Items);
        Assert.Equal(1, database.Store.CountAllItems());
    }

    [Fact]
    public void DeleteItem_tombstones_secret_bookmark_without_plaintext()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        database.Store.DeleteItem("bookmark");

        Assert.Empty(database.Store.Load().Items);
        Assert.Equal(1, database.Store.CountAllItems());
    }

    [Fact]
    public void DeleteItem_tombstones_folder_with_children_and_hides_them_from_load()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateFolder("child-folder", "folder", 1000),
            CreateBookmark("child-bookmark", "child-folder", 1000),
            CreateBookmark("sibling", parentId: null, sortOrder: 500),
        ]));

        database.Store.DeleteItem("folder");

        var visibleItems = database.Store.Load().Items;

        Assert.Single(visibleItems);
        Assert.Equal("sibling", visibleItems[0].Id);
        Assert.Equal(4, database.Store.CountAllItems());
    }

    [Fact]
    public void DeleteItem_rejects_missing_and_tombstoned_items()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
        ]));

        database.Store.DeleteItem("bookmark");

        Assert.Throws<InvalidOperationException>(
            () => database.Store.DeleteItem("missing"));
        Assert.Throws<InvalidOperationException>(
            () => database.Store.DeleteItem("bookmark"));
    }

    [Fact]
    public void MoveToFolderStart_moves_bookmark_to_target_folder_start()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
            CreateFolder("target", parentId: null, sortOrder: 500),
            CreateBookmark("target-existing", "target", 1000),
        ]));

        var moved = database.Store.MoveToFolderStart("bookmark", "target");

        var targetItems = database.Store.Load().Items
            .Where(item => item.ParentId == "target")
            .ToList();

        Assert.Equal("target", moved.ParentId);
        Assert.Equal(2000, moved.SortOrder);
        Assert.Equal(2, moved.Metadata.Revision);
        Assert.Collection(
            targetItems,
            item => Assert.Equal("bookmark", item.Id),
            item => Assert.Equal("target-existing", item.Id));
    }

    [Fact]
    public void MoveToFolderStart_moves_bookmark_to_root_start()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("source", parentId: null, sortOrder: 1000),
            CreateBookmark("bookmark", "source", 1000),
            CreateBookmark("root-existing", parentId: null, sortOrder: 500),
        ]));

        var moved = database.Store.MoveToFolderStart("bookmark", targetParentId: null);

        var rootItems = database.Store.Load().Items
            .Where(item => item.ParentId is null)
            .ToList();

        Assert.Null(moved.ParentId);
        Assert.Equal(2000, moved.SortOrder);
        Assert.Collection(
            rootItems,
            item => Assert.Equal("bookmark", item.Id),
            item => Assert.Equal("source", item.Id),
            item => Assert.Equal("root-existing", item.Id));
    }

    [Fact]
    public void MoveToFolderStart_moves_secret_bookmark_without_decrypting()
    {
        using var database = TempSqliteDatabase.Create();
        database.EnsureSecretProfile();
        var payload = CreateEncryptedPayload();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000) with
            {
                EncryptedPayload = payload
            },
            CreateFolder("target", parentId: null, sortOrder: 500),
        ]));

        var moved = database.Store.MoveToFolderStart("bookmark", "target");

        Assert.True(moved.IsSecret);
        Assert.Equal("target", moved.ParentId);
        Assert.Null(moved.Title);
        Assert.Null(moved.Url);
        AssertEncryptedPayloadEqual(payload, Assert.IsType<EncryptedBookmarkPayloadRecord>(moved.EncryptedPayload));
    }

    [Fact]
    public void MoveToFolderStart_rejects_move_to_same_parent()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateBookmark("bookmark", "folder", 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.MoveToFolderStart("bookmark", "folder"));
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_itself()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.MoveToFolderStart("folder", "folder"));
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_descendant()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.InsertSeedItems(new BookmarkTreeSnapshot(
        [
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateFolder("descendant", "folder", 1000),
        ]));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.MoveToFolderStart("folder", "descendant"));
    }

    [Fact]
    public void Seeder_inserts_sample_data_only_for_new_database_file()
    {
        using var database = TempSqliteDatabase.Create();
        var seeder = new SqliteSampleDataSeeder(database.Store);

        var firstSeed = seeder.SeedIfNewDatabase(
            databaseFileExistedBeforeStartup: false,
            snapshot: new BookmarkTreeSnapshot(
            [
                CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
            ]));
        var secondSeed = seeder.SeedIfNewDatabase(
            databaseFileExistedBeforeStartup: true,
            snapshot: new BookmarkTreeSnapshot(
            [
                CreateBookmark("other", parentId: null, sortOrder: 1000),
            ]));

        Assert.True(firstSeed);
        Assert.False(secondSeed);
        Assert.Equal(1, database.Store.CountAllItems());
        Assert.Equal("bookmark", database.Store.Load().Items.Single().Id);
    }

    [Fact]
    public void Seeder_skips_existing_empty_database_file()
    {
        using var database = TempSqliteDatabase.Create();
        var seeder = new SqliteSampleDataSeeder(database.Store);

        var wasSeeded = seeder.SeedIfNewDatabase(
            databaseFileExistedBeforeStartup: true,
            snapshot: new BookmarkTreeSnapshot(
            [
                CreateBookmark("bookmark", parentId: null, sortOrder: 1000),
            ]));

        Assert.False(wasSeeded);
        Assert.Equal(0, database.Store.CountAllItems());
    }

    private static BookmarkItemRecord CreateFolder(
        string id,
        string? parentId,
        long sortOrder)
    {
        return new(
            id,
            parentId,
            BookmarkItemKind.Folder,
            sortOrder,
            $"Folder {id}",
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateBookmark(
        string id,
        string? parentId,
        long sortOrder)
    {
        return new(
            id,
            parentId,
            BookmarkItemKind.Bookmark,
            sortOrder,
            $"Bookmark {id}",
            $"https://{id}.example.com",
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateSecretBookmark(
        string id,
        string? parentId,
        long sortOrder)
    {
        return new(
            id,
            parentId,
            BookmarkItemKind.Bookmark,
            sortOrder,
            Title: null,
            Url: null,
            IsSecret: true,
            CreateEncryptedPayload(),
            CreateMetadata());
    }

    private static BookmarkItemMetadata CreateMetadata()
    {
        return new(
            CreatedAt,
            CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            "seed-device");
    }

    private static BookmarkIconAssetRecord CreateIconAsset(string id, string sourceHash)
    {
        return new(
            id,
            "sha256",
            sourceHash,
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            ProcessedIconBytes,
            CreatedAt);
    }

    private static void AssertIconAssetEqual(
        BookmarkIconAssetRecord expected,
        BookmarkIconAssetRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.SourceHashAlgorithm, actual.SourceHashAlgorithm);
        Assert.Equal(expected.SourceHash, actual.SourceHash);
        Assert.Equal(expected.SourceSizeBytes, actual.SourceSizeBytes);
        Assert.Equal(expected.ProcessedMimeType, actual.ProcessedMimeType);
        Assert.Equal(expected.ProcessedWidth, actual.ProcessedWidth);
        Assert.Equal(expected.ProcessedHeight, actual.ProcessedHeight);
        Assert.Equal(expected.ProcessedBytes.ToArray(), actual.ProcessedBytes.ToArray());
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
    }

    private static void AssertEncryptedPayloadEqual(
        EncryptedBookmarkPayloadRecord expected,
        EncryptedBookmarkPayloadRecord actual)
    {
        Assert.Equal(expected.Payload.ToArray(), actual.Payload.ToArray());
        Assert.Equal(expected.Nonce.ToArray(), actual.Nonce.ToArray());
        Assert.Equal(expected.CryptoProfileId, actual.CryptoProfileId);
        Assert.Equal(expected.PayloadFormatVersion, actual.PayloadFormatVersion);
    }

    private static EncryptedBookmarkPayloadRecord CreateEncryptedPayload()
    {
        return new(
            Payload: EncryptedPayloadBytes,
            Nonce: EncryptedNonceBytes,
            CryptoProfileId: 1,
            PayloadFormatVersion: 1);
    }

    private static CryptoProfileRecord CreateCryptoProfile()
    {
        return new CryptoProfileRecord(
            SecretCryptoProfileIds.ActiveProfileId,
            SecretEncryptionConstants.CurrentProfileVersion,
            SecretEncryptionConstants.KdfName,
            SecretEncryptionConstants.KdfHashAlgorithm,
            1000,
            KdfSaltBytes,
            SecretEncryptionConstants.KekLengthBytes,
            SecretEncryptionConstants.DataKeyAlgorithm,
            WrappedDataKeyBytes,
            WrappedDataKeyNonceBytes,
            SecretEncryptionConstants.EncryptionAlgorithm,
            SecretEncryptionConstants.PayloadFormat,
            PasswordCheckPayloadBytes,
            PasswordCheckNonceBytes,
            CreatedAt,
            CreatedAt,
            "secret-generation");
    }

    private static readonly byte[] EncryptedPayloadBytes = [10, 20, 30];
    private static readonly byte[] EncryptedNonceBytes = [40, 50, 60];
    private static readonly byte[] KdfSaltBytes = [1, 2, 3];
    private static readonly byte[] WrappedDataKeyBytes = [4, 5, 6];
    private static readonly byte[] WrappedDataKeyNonceBytes = [7, 8, 9];
    private static readonly byte[] PasswordCheckPayloadBytes = [10, 11, 12];
    private static readonly byte[] PasswordCheckNonceBytes = [13, 14, 15];
    private static readonly byte[] ProcessedIconBytes = [1, 2, 3];

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;
        private int _nextId = 1;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
            new SqliteDatabaseMigrator(connectionFactory).Migrate();
            Store = new SqliteBookmarkTreeStore(
                connectionFactory,
                idFactory: () => $"created-{_nextId++}",
                clock: () => UpdatedAt,
                modifiedDeviceId: "test-device");
            SecretProfileStore = new SqliteSecretProfileStore(connectionFactory);
        }

        internal SqliteBookmarkTreeStore Store { get; }

        private SqliteSecretProfileStore SecretProfileStore { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public void EnsureSecretProfile()
        {
            if (SecretProfileStore.LoadActiveProfile() is null)
                SecretProfileStore.SaveNewProfile(CreateCryptoProfile());
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

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
