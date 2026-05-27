using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Stranichnik.ViewModels;

public sealed class BookmarkTreeService
{
    private readonly ObservableCollection<BookmarkTreeItemViewModel> _rootItems;

    public BookmarkTreeService(ObservableCollection<BookmarkTreeItemViewModel> rootItems)
    {
        _rootItems = rootItems;
    }

    public BookmarkTreeAddBookmarkResult AddBookmarkToFolderStart(
        BookmarkFolderViewModel? targetParent,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeInput(title);
        var normalizedUrl = NormalizeInput(url);

        if (normalizedTitle.Length == 0 || normalizedUrl.Length == 0)
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);

        var bookmark = new BookmarkViewModel(normalizedTitle, normalizedUrl)
        {
            Parent = targetParent
        };

        GetMutableItems(targetParent).Insert(0, bookmark);

        return BookmarkTreeAddBookmarkResult.Added(
            bookmark,
            targetParent,
            targetIndex: 0);
    }

    public BookmarkTreeAddFolderResult AddFolderToFolderStart(
        BookmarkFolderViewModel? targetParent,
        string title)
    {
        var normalizedTitle = NormalizeInput(title);

        if (normalizedTitle.Length == 0)
            return BookmarkTreeAddFolderResult.NotAdded(targetParent);

        var folder = new BookmarkFolderViewModel(normalizedTitle)
        {
            Parent = targetParent
        };

        GetMutableItems(targetParent).Insert(0, folder);

        return BookmarkTreeAddFolderResult.Added(
            folder,
            targetParent,
            targetIndex: 0);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This remains an instance service method so persistence, search, and sync dependencies can be added without changing callers.")]
    public BookmarkTreeEditBookmarkResult EditBookmark(
        BookmarkViewModel bookmark,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeInput(title);
        var normalizedUrl = NormalizeInput(url);

        if (normalizedTitle.Length == 0 || normalizedUrl.Length == 0)
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);

        var oldTitle = bookmark.Title;
        var oldUrl = bookmark.Url;

        bookmark.SetTitle(normalizedTitle);
        bookmark.SetUrl(normalizedUrl);

        return BookmarkTreeEditBookmarkResult.Edited(
            bookmark,
            oldTitle,
            oldUrl);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This remains an instance service method so persistence, search, and sync dependencies can be added without changing callers.")]
    public BookmarkTreeEditFolderResult EditFolder(
        BookmarkFolderViewModel folder,
        string title)
    {
        var normalizedTitle = NormalizeInput(title);

        if (folder.IsRoot || normalizedTitle.Length == 0)
            return BookmarkTreeEditFolderResult.NotEdited(folder);

        var oldTitle = folder.Title;
        folder.SetTitle(normalizedTitle);

        return BookmarkTreeEditFolderResult.Edited(folder, oldTitle);
    }

    public BookmarkTreeDeleteResult DeleteItem(BookmarkTreeItemViewModel item)
    {
        if (item is BookmarkFolderViewModel { IsRoot: true })
            return BookmarkTreeDeleteResult.NotDeleted(item);

        var sourceParent = item.Parent;
        var sourceItems = GetMutableItems(sourceParent);
        var sourceIndex = sourceItems.IndexOf(item);

        if (sourceIndex < 0)
            return BookmarkTreeDeleteResult.NotDeleted(item);

        sourceItems.RemoveAt(sourceIndex);
        item.Parent = null;

        return BookmarkTreeDeleteResult.Deleted(item, sourceParent, sourceIndex);
    }

    public bool CanMoveToFolderStart(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        if (item.Parent == targetParent)
            return false;

        if (item == targetParent || IsDescendantOf(targetParent, item))
            return false;

        return GetMutableItems(item.Parent).Contains(item);
    }

    public BookmarkTreeMoveResult MoveToFolderStart(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        if (!CanMoveToFolderStart(item, targetParent))
            return BookmarkTreeMoveResult.NotMoved(item, targetParent);

        var sourceParent = item.Parent;
        var sourceItems = GetMutableItems(sourceParent);
        var targetItems = GetMutableItems(targetParent);
        var sourceIndex = sourceItems.IndexOf(item);

        sourceItems.RemoveAt(sourceIndex);
        item.Parent = targetParent;
        targetItems.Insert(0, item);

        return BookmarkTreeMoveResult.Moved(
            item,
            sourceParent,
            targetParent,
            sourceIndex,
            targetIndex: 0);
    }

    private ObservableCollection<BookmarkTreeItemViewModel> GetMutableItems(BookmarkFolderViewModel? parent)
    {
        return parent?.Children ?? _rootItems;
    }

    private static string NormalizeInput(string value)
    {
        return value.Trim();
    }

    private static bool IsDescendantOf(BookmarkFolderViewModel? possibleDescendant, BookmarkTreeItemViewModel item)
    {
        var current = possibleDescendant?.Parent;

        while (current is not null)
        {
            if (current == item)
                return true;

            current = current.Parent;
        }

        return false;
    }
}

public sealed record BookmarkTreeAddBookmarkResult(
    bool WasAdded,
    BookmarkViewModel? Bookmark,
    BookmarkFolderViewModel? TargetParent,
    int TargetIndex)
{
    public static BookmarkTreeAddBookmarkResult Added(
        BookmarkViewModel bookmark,
        BookmarkFolderViewModel? targetParent,
        int targetIndex)
    {
        return new(true, bookmark, targetParent, targetIndex);
    }

    public static BookmarkTreeAddBookmarkResult NotAdded(BookmarkFolderViewModel? targetParent)
    {
        return new(false, null, targetParent, -1);
    }
}

public sealed record BookmarkTreeAddFolderResult(
    bool WasAdded,
    BookmarkFolderViewModel? Folder,
    BookmarkFolderViewModel? TargetParent,
    int TargetIndex)
{
    public static BookmarkTreeAddFolderResult Added(
        BookmarkFolderViewModel folder,
        BookmarkFolderViewModel? targetParent,
        int targetIndex)
    {
        return new(true, folder, targetParent, targetIndex);
    }

    public static BookmarkTreeAddFolderResult NotAdded(BookmarkFolderViewModel? targetParent)
    {
        return new(false, null, targetParent, -1);
    }
}

public sealed record BookmarkTreeEditBookmarkResult(
    bool WasEdited,
    BookmarkViewModel Bookmark,
    string OldTitle,
    string OldUrl)
{
    public static BookmarkTreeEditBookmarkResult Edited(
        BookmarkViewModel bookmark,
        string oldTitle,
        string oldUrl)
    {
        return new(true, bookmark, oldTitle, oldUrl);
    }

    public static BookmarkTreeEditBookmarkResult NotEdited(BookmarkViewModel bookmark)
    {
        return new(false, bookmark, bookmark.Title, bookmark.Url);
    }
}

public sealed record BookmarkTreeEditFolderResult(
    bool WasEdited,
    BookmarkFolderViewModel Folder,
    string OldTitle)
{
    public static BookmarkTreeEditFolderResult Edited(
        BookmarkFolderViewModel folder,
        string oldTitle)
    {
        return new(true, folder, oldTitle);
    }

    public static BookmarkTreeEditFolderResult NotEdited(BookmarkFolderViewModel folder)
    {
        return new(false, folder, folder.Title);
    }
}

public sealed record BookmarkTreeDeleteResult(
    bool WasDeleted,
    BookmarkTreeItemViewModel Item,
    BookmarkFolderViewModel? SourceParent,
    int SourceIndex)
{
    public static BookmarkTreeDeleteResult Deleted(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? sourceParent,
        int sourceIndex)
    {
        return new(true, item, sourceParent, sourceIndex);
    }

    public static BookmarkTreeDeleteResult NotDeleted(BookmarkTreeItemViewModel item)
    {
        return new(false, item, item.Parent, -1);
    }
}

public sealed record BookmarkTreeMoveResult(
    bool WasMoved,
    BookmarkTreeItemViewModel Item,
    BookmarkFolderViewModel? SourceParent,
    BookmarkFolderViewModel? TargetParent,
    int SourceIndex,
    int TargetIndex)
{
    public static BookmarkTreeMoveResult Moved(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? sourceParent,
        BookmarkFolderViewModel? targetParent,
        int sourceIndex,
        int targetIndex)
    {
        return new(
            true,
            item,
            sourceParent,
            targetParent,
            sourceIndex,
            targetIndex);
    }

    public static BookmarkTreeMoveResult NotMoved(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        return new(
            false,
            item,
            item.Parent,
            targetParent,
            -1,
            -1);
    }
}
