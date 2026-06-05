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

    private static readonly byte[] ProcessedIconBytes = [1, 2, 3];
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
