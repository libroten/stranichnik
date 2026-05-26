using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private bool _isRootDropPlaceholderVisible;
    private bool _isRootDropPlaceholderActive;
    private readonly BookmarkTreeMoveService _moveService;

    public MainWindowViewModel()
    {
        _moveService = new(Items);
    }

    public ObservableCollection<BookmarkTreeItemViewModel> Items { get; } = SampleBookmarksFactory.Create();

    public bool IsRootDropPlaceholderVisible
    {
        get => _isRootDropPlaceholderVisible;
        private set => SetProperty(ref _isRootDropPlaceholderVisible, value);
    }

    public bool IsRootDropPlaceholderActive
    {
        get => _isRootDropPlaceholderActive;
        private set => SetProperty(ref _isRootDropPlaceholderActive, value);
    }

    public bool CanMoveItemToFolder(BookmarkTreeItemViewModel item, BookmarkFolderViewModel? targetParent)
    {
        return _moveService.CanMoveToFolderStart(item, targetParent);
    }

    public BookmarkTreeMoveResult MoveItemToFolderStart(
        BookmarkTreeItemViewModel item,
        BookmarkFolderViewModel? targetParent)
    {
        return _moveService.MoveToFolderStart(item, targetParent);
    }

    public void ShowDropPlaceholders(BookmarkTreeItemViewModel draggedItem)
    {
        foreach (var item in EnumerateItems(Items))
        {
            item.IsDragSource = item == draggedItem;
            item.IsDragDimmed = item != draggedItem;
        }

        IsRootDropPlaceholderVisible = CanMoveItemToFolder(draggedItem, targetParent: null);
        IsRootDropPlaceholderActive = false;

        foreach (var folder in EnumerateFolders(Items))
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

        IsRootDropPlaceholderActive = targetParent is null && IsRootDropPlaceholderVisible;

        foreach (var folder in EnumerateFolders(Items))
            folder.IsDropPlaceholderActive = folder == targetParent && folder.IsDropPlaceholderVisible;
    }

    public void ClearActiveDropPlaceholder()
    {
        IsRootDropPlaceholderActive = false;

        foreach (var folder in EnumerateFolders(Items))
            folder.IsDropPlaceholderActive = false;
    }

    public void SetDragHoverFolder(BookmarkFolderViewModel? targetFolder)
    {
        foreach (var folder in EnumerateFolders(Items))
            folder.IsDragHoverTarget = folder == targetFolder;
    }

    public void ClearDropPlaceholders()
    {
        IsRootDropPlaceholderVisible = false;
        IsRootDropPlaceholderActive = false;

        foreach (var item in EnumerateItems(Items))
        {
            item.IsDragSource = false;
            item.IsDragDimmed = false;
        }

        foreach (var folder in EnumerateFolders(Items))
        {
            folder.IsDropPlaceholderVisible = false;
            folder.IsDropPlaceholderActive = false;
            folder.IsDragHoverTarget = false;
        }
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
    private bool _isDragSource;
    private bool _isDragDimmed;

    protected BookmarkTreeItemViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public BookmarkFolderViewModel? Parent { get; internal set; }

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
        bool isExpanded = false)
        : base(title)
    {
        Children = children is null ? new() : new(children);
        _isExpanded = isExpanded;

        foreach (var child in Children)
            child.Parent = this;
    }

    public ObservableCollection<BookmarkTreeItemViewModel> Children { get; }

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
    public BookmarkViewModel(string title, string url)
        : base(title)
    {
        Url = url;
    }

    public string Url { get; }
}
