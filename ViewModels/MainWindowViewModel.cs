using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Stranichnik.Localization;
using Stranichnik.Storage;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The view model intentionally depends on the storage abstraction so SQLite can replace the in-memory store without changing callers.")]
    private readonly IBookmarkTreeStore _treeStore;

    public MainWindowViewModel()
    {
        _treeStore = new InMemoryBookmarkTreeStore(SampleBookmarkRecordsFactory.Create().Items);
        var items = BookmarkTreeViewModelMapper.CreateViewModels(
            _treeStore.Load(),
            SampleBookmarkRecordsFactory.CreateDefaultExpandedFolderIds());

        RootFolder = new BookmarkFolderViewModel(
            UiStrings.RootAllBookmarks,
            items,
            isExpanded: true,
            isRoot: true);
    }

    public BookmarkFolderViewModel RootFolder { get; }

    public ObservableCollection<BookmarkTreeItemViewModel> Items => RootFolder.Children;

    public bool CanMoveItemToFolder(BookmarkTreeItemViewModel item, BookmarkFolderViewModel? targetParent)
    {
        if (item is BookmarkFolderViewModel { IsRoot: true })
            return false;

        return _treeStore.CanMoveToFolderStart(
            item.Id,
            GetStorageParentId(targetParent));
    }

    public BookmarkTreeMoveResult MoveItemToFolderStart(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        targetParent ??= RootFolder;

        var sourceParent = item.Parent;
        var sourceItems = GetMutableItems(sourceParent);
        var targetItems = GetMutableItems(targetParent);
        var sourceIndex = sourceItems.IndexOf(item);

        if (sourceIndex < 0 || !CanMoveItemToFolder(item, targetParent))
            return BookmarkTreeMoveResult.NotMoved(item, targetParent);

        try
        {
            _treeStore.MoveToFolderStart(
                item.Id,
                GetStorageParentId(targetParent));
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeMoveResult.NotMoved(item, targetParent);
        }

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

    public BookmarkTreeAddBookmarkResult AddBookmarkToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title,
        string url)
    {
        BookmarkItemRecord record;

        try
        {
            record = _treeStore.AddBookmarkToFolderStart(
                GetStorageParentId(targetParent),
                title,
                url);
        }
        catch (ArgumentException)
        {
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);
        }

        var bookmark = new BookmarkViewModel(
            record.Title ?? string.Empty,
            record.Url ?? string.Empty,
            record.Id)
        {
            Parent = targetParent
        };

        targetParent.Children.Insert(0, bookmark);

        return BookmarkTreeAddBookmarkResult.Added(
            bookmark,
            targetParent,
            targetIndex: 0);
    }

    public BookmarkTreeAddFolderResult AddFolderToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title)
    {
        BookmarkItemRecord record;

        try
        {
            record = _treeStore.AddFolderToFolderStart(
                GetStorageParentId(targetParent),
                title);
        }
        catch (ArgumentException)
        {
            return BookmarkTreeAddFolderResult.NotAdded(targetParent);
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeAddFolderResult.NotAdded(targetParent);
        }

        var folder = new BookmarkFolderViewModel(
            record.Title ?? string.Empty,
            isExpanded: false,
            isRoot: false,
            id: record.Id)
        {
            Parent = targetParent
        };

        targetParent.Children.Insert(0, folder);

        return BookmarkTreeAddFolderResult.Added(
            folder,
            targetParent,
            targetIndex: 0);
    }

    public BookmarkTreeEditBookmarkResult EditBookmark(
        BookmarkViewModel bookmark,
        string title,
        string url)
    {
        BookmarkItemRecord record;

        try
        {
            record = _treeStore.EditBookmark(bookmark.Id, title, url);
        }
        catch (ArgumentException)
        {
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);
        }

        var oldTitle = bookmark.Title;
        var oldUrl = bookmark.Url;

        bookmark.SetTitle(record.Title ?? string.Empty);
        bookmark.SetUrl(record.Url ?? string.Empty);

        return BookmarkTreeEditBookmarkResult.Edited(
            bookmark,
            oldTitle,
            oldUrl);
    }

    public BookmarkTreeEditFolderResult EditFolder(
        BookmarkFolderViewModel folder,
        string title)
    {
        if (folder.IsRoot)
            return BookmarkTreeEditFolderResult.NotEdited(folder);

        BookmarkItemRecord record;

        try
        {
            record = _treeStore.EditFolder(folder.Id, title);
        }
        catch (ArgumentException)
        {
            return BookmarkTreeEditFolderResult.NotEdited(folder);
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeEditFolderResult.NotEdited(folder);
        }

        var oldTitle = folder.Title;
        folder.SetTitle(record.Title ?? string.Empty);

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

        try
        {
            _treeStore.DeleteItem(item.Id);
        }
        catch (InvalidOperationException)
        {
            return BookmarkTreeDeleteResult.NotDeleted(item);
        }

        sourceItems.RemoveAt(sourceIndex);
        item.Parent = null;

        return BookmarkTreeDeleteResult.Deleted(item, sourceParent, sourceIndex);
    }

    public void ShowDropPlaceholders(BookmarkTreeItemViewModel draggedItem)
    {
        foreach (var item in EnumerateItems(Items))
        {
            item.IsDragSource = item == draggedItem;
            item.IsDragDimmed = item != draggedItem;
        }

        foreach (var folder in EnumerateFoldersIncludingRoot())
        {
            folder.IsDropPlaceholderVisible = folder.IsExpanded && CanMoveItemToFolder(draggedItem, folder);
            folder.IsDropPlaceholderActive = false;
            folder.IsDragHoverTarget = false;
            folder.IsDragDimmed = !CanMoveItemToFolder(draggedItem, folder) && folder != draggedItem;
        }
    }

    public void ActivateDropPlaceholder(BookmarkFolderViewModel? targetParent)
    {
        ClearActiveDropPlaceholder();

        foreach (var folder in EnumerateFoldersIncludingRoot())
            folder.IsDropPlaceholderActive = folder == targetParent && folder.IsDropPlaceholderVisible;
    }

    public void ClearActiveDropPlaceholder()
    {
        foreach (var folder in EnumerateFoldersIncludingRoot())
            folder.IsDropPlaceholderActive = false;
    }

    public void SetDragHoverFolder(BookmarkFolderViewModel? targetFolder)
    {
        foreach (var folder in EnumerateFoldersIncludingRoot())
            folder.IsDragHoverTarget = folder == targetFolder;
    }

    public void ClearDropPlaceholders()
    {
        foreach (var item in EnumerateItems(Items))
        {
            item.IsDragSource = false;
            item.IsDragDimmed = false;
        }

        foreach (var folder in EnumerateFoldersIncludingRoot())
        {
            folder.IsDragDimmed = false;
            folder.IsDropPlaceholderVisible = false;
            folder.IsDropPlaceholderActive = false;
            folder.IsDragHoverTarget = false;
        }
    }

    private IEnumerable<BookmarkFolderViewModel> EnumerateFoldersIncludingRoot()
    {
        yield return RootFolder;

        foreach (var folder in EnumerateFolders(RootFolder.Children))
            yield return folder;
    }

    private static IEnumerable<BookmarkFolderViewModel> EnumerateFolders(
        IEnumerable<BookmarkTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item is not BookmarkFolderViewModel folder)
                continue;

            yield return folder;

            foreach (var childFolder in EnumerateFolders(folder.Children))
                yield return childFolder;
        }
    }

    private static IEnumerable<BookmarkTreeItemViewModel> EnumerateItems(
        IEnumerable<BookmarkTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            yield return item;

            if (item is BookmarkFolderViewModel folder)
            {
                foreach (var child in EnumerateItems(folder.Children))
                    yield return child;
            }
        }
    }

    private ObservableCollection<BookmarkTreeItemViewModel> GetMutableItems(BookmarkFolderViewModel? parent)
    {
        return parent?.Children ?? Items;
    }

    private static string? GetStorageParentId(BookmarkFolderViewModel? targetParent)
    {
        return targetParent is null || targetParent.IsRoot ? null : targetParent.Id;
    }
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    private string _title;
    private bool _isDragSource;
    private bool _isDragDimmed;

    protected BookmarkTreeItemViewModel(string title, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        _title = title;
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public BookmarkFolderViewModel? Parent { get; internal set; }

    internal void SetTitle(string title)
    {
        Title = title;
    }

    public bool IsDragSource
    {
        get => _isDragSource;
        set => SetProperty(ref _isDragSource, value);
    }

    public bool IsDragDimmed
    {
        get => _isDragDimmed;
        set => SetProperty(ref _isDragDimmed, value);
    }
}

public sealed partial class BookmarkFolderViewModel : BookmarkTreeItemViewModel
{
    private bool _isExpanded;
    private bool _isDropPlaceholderVisible;
    private bool _isDropPlaceholderActive;
    private bool _isDragHoverTarget;

    public BookmarkFolderViewModel(
        string title,
        IEnumerable<BookmarkTreeItemViewModel>? children = null,
        bool isExpanded = false,
        bool isRoot = false,
        string? id = null)
        : base(title, id)
    {
        Children = children is null ? new() : new(children);
        _isExpanded = isExpanded;
        IsRoot = isRoot;

        foreach (var child in Children)
            child.Parent = this;
    }

    public ObservableCollection<BookmarkTreeItemViewModel> Children { get; }

    public bool IsRoot { get; }

    public bool CanShowActions => !IsRoot;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                OnPropertyChanged(nameof(ExpansionGlyph));
        }
    }

    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";

    public bool IsDropPlaceholderVisible
    {
        get => _isDropPlaceholderVisible;
        set => SetProperty(ref _isDropPlaceholderVisible, value);
    }

    public bool IsDropPlaceholderActive
    {
        get => _isDropPlaceholderActive;
        set => SetProperty(ref _isDropPlaceholderActive, value);
    }

    public bool IsDragHoverTarget
    {
        get => _isDragHoverTarget;
        set => SetProperty(ref _isDragHoverTarget, value);
    }
}

public sealed partial class BookmarkViewModel : BookmarkTreeItemViewModel
{
    private string _url;

    public BookmarkViewModel(string title, string url, string? id = null)
        : base(title, id)
    {
        _url = url;
    }

    public string Url
    {
        get => _url;
        private set => SetProperty(ref _url, value);
    }

    internal void SetUrl(string url)
    {
        Url = url;
    }
}
