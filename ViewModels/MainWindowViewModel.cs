using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BookmarkTreeService _treeService;

    public MainWindowViewModel()
    {
        RootFolder = new BookmarkFolderViewModel(
            "Все закладки",
            SampleBookmarksFactory.Create(),
            isExpanded: true,
            isRoot: true);
        _treeService = new(Items);
    }

    public BookmarkFolderViewModel RootFolder { get; }

    public ObservableCollection<BookmarkTreeItemViewModel> Items => RootFolder.Children;

    public bool CanMoveItemToFolder(BookmarkTreeItemViewModel item, BookmarkFolderViewModel? targetParent)
    {
        return _treeService.CanMoveToFolderStart(item, targetParent);
    }

    public BookmarkTreeMoveResult MoveItemToFolderStart(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        return _treeService.MoveToFolderStart(item, targetParent);
    }

    public BookmarkTreeAddBookmarkResult AddBookmarkToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title,
        string url)
    {
        return _treeService.AddBookmarkToFolderStart(targetParent, title, url);
    }

    public BookmarkTreeAddFolderResult AddFolderToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title)
    {
        return _treeService.AddFolderToFolderStart(targetParent, title);
    }

    public BookmarkTreeEditBookmarkResult EditBookmark(
        BookmarkViewModel bookmark,
        string title,
        string url)
    {
        return _treeService.EditBookmark(bookmark, title, url);
    }

    public BookmarkTreeEditFolderResult EditFolder(
        BookmarkFolderViewModel folder,
        string title)
    {
        return _treeService.EditFolder(folder, title);
    }

    public BookmarkTreeDeleteResult DeleteItem(BookmarkTreeItemViewModel item)
    {
        return _treeService.DeleteItem(item);
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
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    private string _title;
    private bool _isDragSource;
    private bool _isDragDimmed;

    protected BookmarkTreeItemViewModel(string title)
    {
        _title = title;
    }

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
        bool isRoot = false)
        : base(title)
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

    public BookmarkViewModel(string title, string url)
        : base(title)
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
