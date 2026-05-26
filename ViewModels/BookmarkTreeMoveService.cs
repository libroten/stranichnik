using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public sealed class BookmarkTreeMoveService
{
    private readonly ObservableCollection<BookmarkTreeItemViewModel> _rootItems;

    public BookmarkTreeMoveService(ObservableCollection<BookmarkTreeItemViewModel> rootItems)
    {
        _rootItems = rootItems;
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
