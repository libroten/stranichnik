using System.Collections.ObjectModel;
using Stranichnik.ViewModels;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkTreeMoveServiceTests
{
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
        var service = new BookmarkTreeMoveService(rootItems);

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
        var service = new BookmarkTreeMoveService(rootItems);

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
        var service = new BookmarkTreeMoveService(rootItems);

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
        var service = new BookmarkTreeMoveService(rootItems);

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
        var service = new BookmarkTreeMoveService(rootItems);

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
        var service = new BookmarkTreeMoveService(rootItems);

        var result = service.MoveToFolderStart(folder, descendant);

        Assert.False(result.WasMoved);
        Assert.Same(folder, rootItems[0]);
        Assert.Same(descendant, folder.Children[0]);
        Assert.Same(folder, descendant.Parent);
        Assert.Null(folder.Parent);
    }
}
