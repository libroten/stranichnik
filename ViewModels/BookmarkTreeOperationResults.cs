namespace Stranichnik.ViewModels;

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
