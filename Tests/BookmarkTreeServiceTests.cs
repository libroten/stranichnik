using System.Collections.ObjectModel;
using Stranichnik.ViewModels;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkTreeServiceTests
{
    [Fact]
    public void AddBookmarkToFolderStart_adds_bookmark_to_folder_start()
    {
        var existingBookmark = new BookmarkViewModel("Existing", "https://existing.example.com");
        var folder = new BookmarkFolderViewModel("Folder", new[] { existingBookmark });
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.AddBookmarkToFolderStart(folder, " Docs ", " https://docs.example.com ");

        Assert.True(result.WasAdded);
        var addedBookmark = Assert.IsType<BookmarkViewModel>(result.Bookmark);
        Assert.Same(addedBookmark, folder.Children[0]);
        Assert.Same(existingBookmark, folder.Children[1]);
        Assert.Same(folder, addedBookmark.Parent);
        Assert.Equal("Docs", addedBookmark.Title);
        Assert.Equal("https://docs.example.com", addedBookmark.Url);
        Assert.Same(folder, result.TargetParent);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void AddFolderToFolderStart_adds_folder_to_root_start()
    {
        var existingFolder = new BookmarkFolderViewModel("Existing");
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            existingFolder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.AddFolderToFolderStart(targetParent: null, " New Folder ");

        Assert.True(result.WasAdded);
        var addedFolder = Assert.IsType<BookmarkFolderViewModel>(result.Folder);
        Assert.Same(addedFolder, rootItems[0]);
        Assert.Same(existingFolder, rootItems[1]);
        Assert.Null(addedFolder.Parent);
        Assert.Equal("New Folder", addedFolder.Title);
        Assert.Null(result.TargetParent);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void EditBookmark_updates_title_and_url()
    {
        var bookmark = new BookmarkViewModel("Old", "https://old.example.com");
        var service = new BookmarkTreeService(new());

        var result = service.EditBookmark(bookmark, " New ", " https://new.example.com ");

        Assert.True(result.WasEdited);
        Assert.Same(bookmark, result.Bookmark);
        Assert.Equal("Old", result.OldTitle);
        Assert.Equal("https://old.example.com", result.OldUrl);
        Assert.Equal("New", bookmark.Title);
        Assert.Equal("https://new.example.com", bookmark.Url);
    }

    [Fact]
    public void EditFolder_rejects_root_folder()
    {
        var rootFolder = new BookmarkFolderViewModel("Root", isRoot: true);
        var service = new BookmarkTreeService(new());

        var result = service.EditFolder(rootFolder, "New Root");

        Assert.False(result.WasEdited);
        Assert.Equal("Root", rootFolder.Title);
    }

    [Fact]
    public void DeleteItem_removes_bookmark_from_parent()
    {
        var bookmark = new BookmarkViewModel("Docs", "https://docs.example.com");
        var folder = new BookmarkFolderViewModel("Folder", new[] { bookmark });
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.DeleteItem(bookmark);

        Assert.True(result.WasDeleted);
        Assert.Empty(folder.Children);
        Assert.Null(bookmark.Parent);
        Assert.Same(bookmark, result.Item);
        Assert.Same(folder, result.SourceParent);
        Assert.Equal(0, result.SourceIndex);
    }

    [Fact]
    public void MoveToFolderStart_moves_bookmark_to_target_folder_start()
    {
        var bookmark = new BookmarkViewModel("Docs", "https://docs.example.com");
        var targetExistingBookmark = new BookmarkViewModel("Existing", "https://existing.example.com");
        var targetFolder = new BookmarkFolderViewModel("Target", new[] { targetExistingBookmark });
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            bookmark,
            targetFolder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(bookmark, targetFolder);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, targetFolder.Children[0]);
        Assert.Same(targetExistingBookmark, targetFolder.Children[1]);
        Assert.DoesNotContain(bookmark, rootItems);
        Assert.Same(targetFolder, bookmark.Parent);
        Assert.Null(result.SourceParent);
        Assert.Same(targetFolder, result.TargetParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void MoveToFolderStart_moves_bookmark_to_root_start()
    {
        var bookmark = new BookmarkViewModel("Docs", "https://docs.example.com");
        var folder = new BookmarkFolderViewModel("Folder", new[] { bookmark });
        var rootExistingBookmark = new BookmarkViewModel("Root", "https://root.example.com");
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            rootExistingBookmark,
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(bookmark, targetParent: null);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, rootItems[0]);
        Assert.Same(rootExistingBookmark, rootItems[1]);
        Assert.DoesNotContain(bookmark, folder.Children);
        Assert.Null(bookmark.Parent);
        Assert.Same(folder, result.SourceParent);
        Assert.Null(result.TargetParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void MoveToFolderStart_moves_folder_with_children()
    {
        var child = new BookmarkViewModel("Child", "https://child.example.com");
        var movingFolder = new BookmarkFolderViewModel("Moving", new[] { child });
        var targetFolder = new BookmarkFolderViewModel("Target");
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            movingFolder,
            targetFolder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(movingFolder, targetFolder);

        Assert.True(result.WasMoved);
        Assert.Same(movingFolder, targetFolder.Children[0]);
        Assert.Same(targetFolder, movingFolder.Parent);
        Assert.Same(movingFolder, child.Parent);
        Assert.DoesNotContain(movingFolder, rootItems);
    }

    [Fact]
    public void MoveToFolderStart_rejects_move_to_same_parent()
    {
        var bookmark = new BookmarkViewModel("Docs", "https://docs.example.com");
        var folder = new BookmarkFolderViewModel("Folder", new[] { bookmark });
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(bookmark, folder);

        Assert.False(result.WasMoved);
        Assert.Same(bookmark, folder.Children[0]);
        Assert.Same(folder, bookmark.Parent);
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_itself()
    {
        var folder = new BookmarkFolderViewModel("Folder");
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(folder, folder);

        Assert.False(result.WasMoved);
        Assert.Same(folder, rootItems[0]);
        Assert.Null(folder.Parent);
    }

    [Fact]
    public void MoveToFolderStart_rejects_folder_move_to_descendant()
    {
        var descendant = new BookmarkFolderViewModel("Descendant");
        var folder = new BookmarkFolderViewModel("Folder", new[] { descendant });
        var rootItems = new ObservableCollection<BookmarkTreeItemViewModel>
        {
            folder
        };
        var service = new BookmarkTreeService(rootItems);

        var result = service.MoveToFolderStart(folder, descendant);

        Assert.False(result.WasMoved);
        Assert.Same(folder, rootItems[0]);
        Assert.Same(descendant, folder.Children[0]);
        Assert.Same(folder, descendant.Parent);
        Assert.Null(folder.Parent);
    }
}
