using System;
using System.Linq;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class InMemoryBookmarkTreeStoreTests
{
    [Fact]
    public void AddBookmarkToFolderStart_adds_bookmark_to_folder_start()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var existingBookmark = CreateBookmark("existing", "folder", 1000);
        var store = CreateStore(folder, existingBookmark);

        var bookmark = store.AddBookmarkToFolderStart("folder", " Docs ", " https://docs.example.com ");

        var folderBookmarks = store.Load().Items
            .Where(item => item.ParentId == "folder")
            .ToList();

        Assert.Equal("Docs", bookmark.Title);
        Assert.Equal("https://docs.example.com", bookmark.Url);
        Assert.Equal(BookmarkItemKind.Bookmark, bookmark.Kind);
        Assert.Equal("folder", bookmark.ParentId);
        Assert.Equal(2000, bookmark.SortOrder);
        Assert.Equal("created-1", bookmark.Id);
        Assert.Same(bookmark, folderBookmarks[0]);
        Assert.Same(existingBookmark, folderBookmarks[1]);
    }

    [Fact]
    public void AddFolderToFolderStart_adds_folder_to_root_start()
    {
        var existingFolder = CreateFolder("existing", parentId: null, sortOrder: 1000);
        var store = CreateStore(existingFolder);

        var folder = store.AddFolderToFolderStart(parentId: null, " New Folder ");

        var rootItems = store.Load().Items
            .Where(item => item.ParentId is null)
            .ToList();

        Assert.Equal("New Folder", folder.Title);
        Assert.Equal(BookmarkItemKind.Folder, folder.Kind);
        Assert.Null(folder.ParentId);
        Assert.Equal(2000, folder.SortOrder);
        Assert.Equal("created-1", folder.Id);
        Assert.Same(folder, rootItems[0]);
        Assert.Same(existingFolder, rootItems[1]);
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_adds_secret_bookmark_without_plaintext()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var store = CreateStore(folder);
        var payload = CreateEncryptedPayload();

        var bookmark = store.AddSecretBookmarkToFolderStart("folder", "created-1", payload, "generation-1");

        Assert.Equal(BookmarkItemKind.Bookmark, bookmark.Kind);
        Assert.Equal("folder", bookmark.ParentId);
        Assert.Equal("created-1", bookmark.Id);
        Assert.True(bookmark.IsSecret);
        Assert.Null(bookmark.Title);
        Assert.Null(bookmark.Url);
        Assert.Null(bookmark.IconAssetId);
        Assert.Null(bookmark.SecretIconAssetId);
        Assert.Same(payload, bookmark.EncryptedPayload);
        Assert.Same(bookmark, store.Load().Items.First(item => item.Id == "created-1"));
    }

    [Fact]
    public void EditBookmark_updates_title_url_and_metadata()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);

        var edited = store.EditBookmark("bookmark", " New ", " https://new.example.com ");

        Assert.Equal("New", edited.Title);
        Assert.Equal("https://new.example.com", edited.Url);
        Assert.Equal(2, edited.Metadata.Revision);
        Assert.Equal(UpdatedAt, edited.Metadata.UpdatedAtUtc);
        Assert.Equal(BookmarkSyncState.Dirty, edited.Metadata.SyncState);
    }

    [Fact]
    public void EditBookmarkAsSecret_clears_plaintext_and_icon()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000) with
        {
            IconAssetId = "icon"
        };
        var store = CreateStore(bookmark);
        store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));
        var payload = CreateEncryptedPayload();

        var edited = store.EditBookmarkAsSecret("bookmark", payload, "generation-1");

        Assert.True(edited.IsSecret);
        Assert.Null(edited.Title);
        Assert.Null(edited.Url);
        Assert.Null(edited.IconAssetId);
        Assert.Same(payload, edited.EncryptedPayload);
        Assert.Equal(2, edited.Metadata.Revision);
    }

    [Fact]
    public void EditSecretBookmarkAsPlaintext_restores_plaintext_and_clears_secret_payload()
    {
        var bookmark = CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);

        var edited = store.EditSecretBookmarkAsPlaintext("bookmark", " Title ", " https://example.com ");

        Assert.False(edited.IsSecret);
        Assert.Equal("Title", edited.Title);
        Assert.Equal("https://example.com", edited.Url);
        Assert.Null(edited.EncryptedPayload);
        Assert.Null(edited.IconAssetId);
        Assert.Null(edited.SecretIconAssetId);
        Assert.Equal(2, edited.Metadata.Revision);
    }

    [Fact]
    public void EditSecretBookmarkAsPlaintext_rejects_normal_bookmark()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);

        Assert.Throws<InvalidOperationException>(
            () => store.EditSecretBookmarkAsPlaintext("bookmark", "Title", "https://example.com"));
    }

    [Fact]
    public void EditBookmarkAsSecret_rejects_folder()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var store = CreateStore(folder);

        Assert.Throws<InvalidOperationException>(
            () => store.EditBookmarkAsSecret("folder", CreateEncryptedPayload(), "generation-1"));
    }

    [Fact]
    public void EditFolder_updates_title_and_metadata()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var store = CreateStore(folder);

        var edited = store.EditFolder("folder", " New Folder ");

        Assert.Equal("New Folder", edited.Title);
        Assert.Equal(2, edited.Metadata.Revision);
        Assert.Equal(UpdatedAt, edited.Metadata.UpdatedAtUtc);
        Assert.Equal(BookmarkSyncState.Dirty, edited.Metadata.SyncState);
    }

    [Fact]
    public void GetOrCreateIconAsset_reuses_existing_asset_with_same_source_hash()
    {
        var store = CreateStore();
        var first = CreateIconAsset("icon-1", "same-hash");
        var second = CreateIconAsset("icon-2", "same-hash");

        var created = store.GetOrCreateIconAsset(first);
        var reused = store.GetOrCreateIconAsset(second);

        Assert.Same(first, created);
        Assert.Same(first, reused);
        Assert.Same(first, store.GetIconAsset("icon-1"));
        Assert.Same(first, store.GetIconAssetBySourceHash("sha256", "same-hash"));
        Assert.Null(store.GetIconAsset("icon-2"));
    }

    [Fact]
    public void GetOrCreateSecretIconAsset_reuses_existing_asset_with_same_source_hash()
    {
        var store = CreateStore();
        var first = CreateSecretIconAsset("secret-icon-1", "same-hash");
        var second = CreateSecretIconAsset("secret-icon-2", "same-hash");

        var created = store.GetOrCreateSecretIconAsset(first);
        var reused = store.GetOrCreateSecretIconAsset(second);

        Assert.Same(first, created);
        Assert.Same(first, reused);
        Assert.Same(first, store.GetSecretIconAsset("secret-icon-1"));
        Assert.Same(first, store.GetSecretIconAssetBySourceHash("sha256", "same-hash"));
        Assert.Null(store.GetSecretIconAsset("secret-icon-2"));
    }

    [Fact]
    public void SetItemIconAsset_updates_and_clears_icon_reference()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);
        store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));

        var withIcon = store.SetItemIconAsset("bookmark", "icon");
        var withoutIcon = store.SetItemIconAsset("bookmark", iconAssetId: null);

        Assert.Equal("icon", withIcon.IconAssetId);
        Assert.Null(withoutIcon.IconAssetId);
        Assert.Equal(3, withoutIcon.Metadata.Revision);
    }

    [Fact]
    public void SetItemIconAsset_rejects_secret_bookmark_custom_icon()
    {
        var bookmark = CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);
        store.GetOrCreateIconAsset(CreateIconAsset("icon", "hash"));

        Assert.Throws<InvalidOperationException>(
            () => store.SetItemIconAsset("bookmark", "icon"));
        Assert.Throws<InvalidOperationException>(
            () => store.SetItemIconAsset("bookmark", iconAssetId: null));
    }

    [Fact]
    public void SetItemSecretIconAsset_updates_secret_bookmark_and_clears_plaintext_icon_reference()
    {
        var bookmark = CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "hash"));

        var withIcon = store.SetItemSecretIconAsset("bookmark", "secret-icon");
        var withoutIcon = store.SetItemSecretIconAsset("bookmark", secretIconAssetId: null);

        Assert.Equal("secret-icon", withIcon.SecretIconAssetId);
        Assert.Null(withIcon.IconAssetId);
        Assert.Null(withoutIcon.SecretIconAssetId);
        Assert.Equal(3, withoutIcon.Metadata.Revision);
    }

    [Fact]
    public void SetItemSecretIconAsset_rejects_plaintext_bookmark_custom_secret_icon()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "hash"));

        Assert.Throws<InvalidOperationException>(
            () => store.SetItemSecretIconAsset("bookmark", "secret-icon"));
        Assert.Throws<InvalidOperationException>(
            () => store.SetItemSecretIconAsset("bookmark", secretIconAssetId: null));
    }

    [Fact]
    public void DeleteItem_does_not_delete_shared_icon_asset()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);
        var iconAsset = CreateIconAsset("icon", "hash");
        store.GetOrCreateIconAsset(iconAsset);
        store.SetItemIconAsset("bookmark", "icon");

        store.DeleteItem("bookmark");

        Assert.Same(iconAsset, store.GetIconAsset("icon"));
    }

    [Fact]
    public void DeleteItem_tombstones_bookmark_and_hides_it_from_load()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);

        store.DeleteItem("bookmark");

        Assert.Empty(store.Load().Items);
    }

    [Fact]
    public void DeleteItem_tombstones_folder_with_children_and_hides_them_from_load()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var childFolder = CreateFolder("child-folder", "folder", 1000);
        var childBookmark = CreateBookmark("child-bookmark", "child-folder", 1000);
        var siblingBookmark = CreateBookmark("sibling", parentId: null, sortOrder: 500);
        var store = CreateStore(folder, childFolder, childBookmark, siblingBookmark);

        store.DeleteItem("folder");

        var visibleItems = store.Load().Items;

        Assert.Single(visibleItems);
        Assert.Same(siblingBookmark, visibleItems[0]);
    }

    [Fact]
    public void MoveToFolderStart_moves_bookmark_to_target_folder_start()
    {
        var bookmark = CreateBookmark("bookmark", parentId: null, sortOrder: 1000);
        var targetFolder = CreateFolder("target", parentId: null, sortOrder: 500);
        var targetExistingBookmark = CreateBookmark("target-existing", "target", 1000);
        var store = CreateStore(bookmark, targetFolder, targetExistingBookmark);

        var moved = store.MoveToFolderStart("bookmark", "target");

        var targetItems = store.Load().Items
            .Where(item => item.ParentId == "target")
            .ToList();

        Assert.Equal("target", moved.ParentId);
        Assert.Equal(2000, moved.SortOrder);
        Assert.Equal(2, moved.Metadata.Revision);
        Assert.Same(moved, targetItems[0]);
        Assert.Same(targetExistingBookmark, targetItems[1]);
    }

    [Fact]
    public void MoveToFolderStart_moves_secret_bookmark_without_decrypting()
    {
        var bookmark = CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000);
        var targetFolder = CreateFolder("target", parentId: null, sortOrder: 500);
        var store = CreateStore(bookmark, targetFolder);

        var moved = store.MoveToFolderStart("bookmark", "target");

        Assert.True(moved.IsSecret);
        Assert.Equal("target", moved.ParentId);
        Assert.Same(bookmark.EncryptedPayload, moved.EncryptedPayload);
        Assert.Null(moved.Title);
        Assert.Null(moved.Url);
    }

    [Fact]
    public void DeleteItem_tombstones_secret_bookmark_without_plaintext()
    {
        var bookmark = CreateSecretBookmark("bookmark", parentId: null, sortOrder: 1000);
        var store = CreateStore(bookmark);

        store.DeleteItem("bookmark");

        Assert.Empty(store.Load().Items);
    }

    [Fact]
    public void MoveToFolderStart_rejects_move_to_same_parent()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var bookmark = CreateBookmark("bookmark", "folder", 1000);
        var store = CreateStore(folder, bookmark);

        Assert.Throws<InvalidOperationException>(
            () => store.MoveToFolderStart("bookmark", "folder"));
    }

    [Fact]
    public void CanMoveToFolderStart_rejects_move_to_same_parent()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var bookmark = CreateBookmark("bookmark", "folder", 1000);
        var store = CreateStore(folder, bookmark);

        Assert.False(store.CanMoveToFolderStart("bookmark", "folder"));
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_itself()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var store = CreateStore(folder);

        Assert.Throws<InvalidOperationException>(
            () => store.MoveToFolderStart("folder", "folder"));
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_descendant()
    {
        var folder = CreateFolder("folder", parentId: null, sortOrder: 1000);
        var descendant = CreateFolder("descendant", "folder", 1000);
        var store = CreateStore(folder, descendant);

        Assert.Throws<InvalidOperationException>(
            () => store.MoveToFolderStart("folder", "descendant"));
    }

    private static InMemoryBookmarkTreeStore CreateStore(params BookmarkItemRecord[] items)
    {
        var nextId = 1;

        return new(
            items,
            idFactory: () => $"created-{nextId++}",
            clock: () => UpdatedAt,
            modifiedDeviceId: "test-device");
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
            SecretGenerationId: null,
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
            SecretGenerationId: null,
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
            "generation",
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
            ContentHash: null,
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

    private static SecretIconAssetRecord CreateSecretIconAsset(string id, string sourceHash)
    {
        return new(
            id,
            "sha256",
            sourceHash,
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            new EncryptedSecretIconPayloadRecord(
                Payload: EncryptedPayloadBytes,
                Nonce: EncryptedNonceBytes,
                PayloadFormatVersion: 1),
            "generation",
            CreatedAt);
    }

    private static EncryptedBookmarkPayloadRecord CreateEncryptedPayload()
    {
        return new(
            Payload: EncryptedPayloadBytes,
            Nonce: EncryptedNonceBytes,
            CryptoProfileId: 1,
            PayloadFormatVersion: 1);
    }

    private static readonly byte[] EncryptedPayloadBytes = [10, 20, 30];
    private static readonly byte[] EncryptedNonceBytes = [40, 50, 60];
    private static readonly byte[] ProcessedIconBytes = [1, 2, 3];
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
