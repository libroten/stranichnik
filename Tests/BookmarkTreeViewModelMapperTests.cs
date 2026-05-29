using System.Linq;
using Stranichnik.Storage;
using Stranichnik.ViewModels;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkTreeViewModelMapperTests
{
    [Fact]
    public void CreateViewModels_builds_tree_from_snapshot()
    {
        var snapshot = new BookmarkTreeSnapshot(new[]
        {
            CreateFolder("folder", parentId: null, sortOrder: 1000),
            CreateBookmark("bookmark", "folder", sortOrder: 1000)
        });

        var items = BookmarkTreeViewModelMapper.CreateViewModels(snapshot);

        var folder = Assert.IsType<BookmarkFolderViewModel>(Assert.Single(items));
        var bookmark = Assert.IsType<BookmarkViewModel>(Assert.Single(folder.Children));

        Assert.Equal("folder", folder.Id);
        Assert.Equal("bookmark", bookmark.Id);
        Assert.Same(folder, bookmark.Parent);
        Assert.Equal("Folder folder", folder.Title);
        Assert.Equal("Bookmark bookmark", bookmark.Title);
        Assert.Equal("https://bookmark.example.com", bookmark.Url);
    }

    [Fact]
    public void CreateViewModels_orders_siblings_by_sort_order_descending()
    {
        var snapshot = new BookmarkTreeSnapshot(new[]
        {
            CreateBookmark("old", parentId: null, sortOrder: 1000),
            CreateBookmark("new", parentId: null, sortOrder: 2000)
        });

        var items = BookmarkTreeViewModelMapper.CreateViewModels(snapshot);

        Assert.Equal(ExpectedDescendingItemIds, items.Select(item => item.Id));
    }

    [Fact]
    public void SampleBookmarkRecordsFactory_creates_expected_sample_shape()
    {
        var snapshot = SampleBookmarkRecordsFactory.Create();

        Assert.Contains(snapshot.Items, item => item.Id == "work" && item.Kind == BookmarkItemKind.Folder);
        Assert.Contains(snapshot.Items, item => item.Id == "long-root-bookmark" && item.Kind == BookmarkItemKind.Bookmark);
        Assert.Contains(snapshot.Items, item => item.Id == "deep-level-20" && item.Kind == BookmarkItemKind.Folder);
        Assert.Contains(snapshot.Items, item => item.Id == "scroll-test" && item.Kind == BookmarkItemKind.Folder);
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
            System.DateTimeOffset.UnixEpoch,
            System.DateTimeOffset.UnixEpoch,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            "test");
    }

    private static readonly string[] ExpectedDescendingItemIds = ["new", "old"];
}
