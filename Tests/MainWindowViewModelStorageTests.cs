using System.Linq;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Storage;
using Stranichnik.ViewModels;
using Xunit;

namespace Stranichnik.Tests;

public sealed class MainWindowViewModelStorageTests
{
    [Fact]
    public void AddBookmarkToFolderStart_adds_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddBookmarkToFolderStart(
            targetFolder,
            " New Docs ",
            " https://new-docs.example.com ");

        var bookmark = Assert.IsType<BookmarkViewModel>(result.Bookmark);

        Assert.True(result.WasAdded);
        Assert.Same(bookmark, targetFolder.Children[0]);
        Assert.Same(targetFolder, bookmark.Parent);
        Assert.Equal("New Docs", bookmark.Title);
        Assert.Equal("https://new-docs.example.com", bookmark.Url);
        Assert.False(string.IsNullOrWhiteSpace(bookmark.Id));
        Assert.Equal(0, result.TargetIndex);
        Assert.Same(targetFolder, result.TargetParent);
    }

    [Fact]
    public void AddFolderToFolderStart_adds_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddFolderToFolderStart(
            targetFolder,
            " New Folder ");

        var folder = Assert.IsType<BookmarkFolderViewModel>(result.Folder);

        Assert.True(result.WasAdded);
        Assert.Same(folder, targetFolder.Children[0]);
        Assert.Same(targetFolder, folder.Parent);
        Assert.Equal("New Folder", folder.Title);
        Assert.False(folder.IsExpanded);
        Assert.False(string.IsNullOrWhiteSpace(folder.Id));
        Assert.Equal(0, result.TargetIndex);
        Assert.Same(targetFolder, result.TargetParent);
    }

    [Fact]
    public void EditBookmark_updates_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.EditBookmark(
            bookmark,
            " New Avalonia ",
            " https://new-avalonia.example.com ");

        Assert.True(result.WasEdited);
        Assert.Same(bookmark, result.Bookmark);
        Assert.Equal("Avalonia Docs", result.OldTitle);
        Assert.Equal("https://docs.avaloniaui.net/", result.OldUrl);
        Assert.Equal("New Avalonia", bookmark.Title);
        Assert.Equal("https://new-avalonia.example.com", bookmark.Url);
    }

    [Fact]
    public void EditFolder_updates_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.EditFolder(folder, " New Work ");

        Assert.True(result.WasEdited);
        Assert.Same(folder, result.Folder);
        Assert.Equal("Work", result.OldTitle);
        Assert.Equal("New Work", folder.Title);
    }

    [Fact]
    public void EditFolder_rejects_root_folder()
    {
        var viewModel = CreateViewModel();
        var oldTitle = viewModel.RootFolder.Title;

        var result = viewModel.EditFolder(viewModel.RootFolder, "New Root");

        Assert.False(result.WasEdited);
        Assert.Equal(oldTitle, viewModel.RootFolder.Title);
    }

    [Fact]
    public void DeleteItem_removes_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            folder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.DeleteItem(bookmark);

        Assert.True(result.WasDeleted);
        Assert.Same(bookmark, result.Item);
        Assert.Same(folder, result.SourceParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.DoesNotContain(bookmark, folder.Children);
        Assert.Null(bookmark.Parent);
    }

    [Fact]
    public void DeleteItem_removes_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.DeleteItem(folder);

        Assert.True(result.WasDeleted);
        Assert.Same(folder, result.Item);
        Assert.Same(viewModel.RootFolder, result.SourceParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.DoesNotContain(folder, viewModel.Items);
        Assert.Null(folder.Parent);
    }

    [Fact]
    public void DeleteItem_rejects_root_folder()
    {
        var viewModel = CreateViewModel();

        var result = viewModel.DeleteItem(viewModel.RootFolder);

        Assert.False(result.WasDeleted);
        Assert.NotNull(viewModel.RootFolder);
    }

    [Fact]
    public void MoveItemToFolderStart_moves_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var bookmark = Assert.IsType<BookmarkViewModel>(
            viewModel.Items.First(item => item.Id == "openai"));
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.MoveItemToFolderStart(bookmark, targetFolder);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, targetFolder.Children[0]);
        Assert.Same(targetFolder, bookmark.Parent);
        Assert.DoesNotContain(bookmark, viewModel.Items);
        Assert.Same(viewModel.RootFolder, result.SourceParent);
        Assert.Same(targetFolder, result.TargetParent);
        Assert.Equal(4, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void MoveItemToFolderStart_treats_null_target_as_root_folder()
    {
        var viewModel = CreateViewModel();
        var sourceFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            sourceFolder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.MoveItemToFolderStart(bookmark, targetParent: null);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, viewModel.Items[0]);
        Assert.Same(viewModel.RootFolder, bookmark.Parent);
        Assert.Same(sourceFolder, result.SourceParent);
        Assert.Same(viewModel.RootFolder, result.TargetParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void CanMoveItemToFolder_rejects_folder_move_to_descendant()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "deep-level-1"));
        var descendant = Assert.IsType<BookmarkFolderViewModel>(
            folder.Children.First(item => item.Id == "deep-level-2"));

        Assert.False(viewModel.CanMoveItemToFolder(folder, descendant));
    }

    [Fact]
    public void Constructor_rebuilds_search_from_initial_snapshot()
    {
        var viewModel = CreateViewModel();

        var results = viewModel.SearchBookmarks("avaloniaui");

        Assert.Equal("avalonia-docs", Assert.Single(results).Id);
    }

    [Fact]
    public void AddBookmarkToFolderStart_updates_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddBookmarkToFolderStart(
            targetFolder,
            "Zephyr Manuals",
            "https://zephyr-manuals.example.com");

        var searchResult = Assert.Single(viewModel.SearchBookmarks("zephyr manuals"));

        Assert.True(result.WasAdded);
        Assert.Equal(result.Bookmark?.Id, searchResult.Id);
    }

    [Fact]
    public void EditBookmark_updates_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        viewModel.EditBookmark(
            bookmark,
            "Renamed Search Target",
            "https://renamed.example.com");

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
        Assert.Equal("avalonia-docs", Assert.Single(viewModel.SearchBookmarks("renamed target")).Id);
    }

    [Fact]
    public void DeleteItem_removes_bookmark_from_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        viewModel.DeleteItem(bookmark);

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
    }

    [Fact]
    public void DeleteItem_removes_folder_descendants_from_search_index()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        viewModel.DeleteItem(folder);

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
        Assert.Empty(viewModel.SearchBookmarks("nuget"));
    }

    [Fact]
    public void SearchQuery_populates_display_results()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchQuery = "github";

        var result = Assert.Single(viewModel.SearchResults);
        Assert.True(viewModel.IsSearchActive);
        Assert.False(viewModel.IsBookmarksTreeVisible);
        Assert.True(viewModel.HasSearchResults);
        Assert.False(viewModel.HasNoSearchResults);
        Assert.Equal("github", result.Id);
        Assert.Equal("GitHub", result.Title);
        Assert.Equal("https://github.com/", result.Url);
    }

    [Fact]
    public void SearchQuery_shows_empty_state_when_no_results_match()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchQuery = "zzzzzzzzzzzz";

        Assert.Empty(viewModel.SearchResults);
        Assert.True(viewModel.IsSearchActive);
        Assert.False(viewModel.IsBookmarksTreeVisible);
        Assert.False(viewModel.HasSearchResults);
        Assert.True(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void ClearSearch_clears_display_results_and_shows_tree()
    {
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "github";

        viewModel.ClearSearch();

        Assert.Empty(viewModel.SearchResults);
        Assert.False(viewModel.IsSearchActive);
        Assert.True(viewModel.IsBookmarksTreeVisible);
        Assert.False(viewModel.HasSearchResults);
        Assert.False(viewModel.HasNoSearchResults);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            new InMemoryBookmarkTreeStore(SampleBookmarkRecordsFactory.Create().Items),
            new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
            expandedFolderIds: SampleBookmarkRecordsFactory.CreateDefaultExpandedFolderIds());
    }
}
