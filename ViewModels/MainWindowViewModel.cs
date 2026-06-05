using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Media;
using Stranichnik.Icons;
using Stranichnik.Localization;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Storage;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The view model intentionally depends on the storage abstraction so SQLite can replace the in-memory store without changing callers.")]
    private readonly IBookmarkTreeStore _treeStore;
    private readonly BookmarkSearchService _searchService;
    private readonly BookmarkIconImageCache _iconImageCache;
    private readonly IconAssetService _iconAssetService;
    private readonly Dictionary<string, BookmarkViewModel> _bookmarkViewModelsById = new(StringComparer.Ordinal);
    private string _searchQuery = string.Empty;

    public MainWindowViewModel(
        IBookmarkTreeStore treeStore,
        BookmarkSearchService searchService,
        BookmarkIconImageCache? iconImageCache = null,
        IReadOnlySet<string>? expandedFolderIds = null)
    {
        ArgumentNullException.ThrowIfNull(treeStore);
        ArgumentNullException.ThrowIfNull(searchService);

        _treeStore = treeStore;
        _searchService = searchService;
        _iconImageCache = iconImageCache ?? new BookmarkIconImageCache(treeStore);
        _iconAssetService = new IconAssetService(_treeStore, new IconImageProcessor());

        var snapshot = _treeStore.Load();
        _searchService.Rebuild(snapshot);

        var items = BookmarkTreeViewModelMapper.CreateViewModels(
            snapshot,
            expandedFolderIds,
            _iconImageCache);

        RootFolder = new BookmarkFolderViewModel(
            UiStrings.RootAllBookmarks,
            items,
            isExpanded: true,
            isRoot: true);

        RebuildBookmarkLookup();
    }

    public BookmarkFolderViewModel RootFolder { get; }

    public ObservableCollection<BookmarkTreeItemViewModel> Items => RootFolder.Children;

    public ObservableCollection<BookmarkSearchResultItem> SearchResults { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
                return;

            UpdateSearchResults();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool IsBookmarksTreeVisible => !IsSearchActive;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasNoSearchResults => IsSearchActive && SearchResults.Count == 0;

    public IReadOnlyList<BookmarkSearchResult> SearchBookmarks(
        string query,
        BookmarkSearchOptions? options = null)
    {
        return _searchService.Search(query, options);
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

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
        string url,
        BookmarkIconSelection? iconSelection = null)
    {
        if (!TryPrepareIconSelection(iconSelection, out var iconAssetId, out var shouldSetIcon))
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);

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

        if (shouldSetIcon)
            record = _treeStore.SetItemIconAsset(record.Id, iconAssetId);

        var bookmark = new BookmarkViewModel(
            record.Title ?? string.Empty,
            record.Url ?? string.Empty,
            record.Id,
            _iconImageCache.GetImage(record.IconAssetId))
        {
            Parent = targetParent
        };

        targetParent.Children.Insert(0, bookmark);
        _bookmarkViewModelsById[bookmark.Id] = bookmark;
        _searchService.AddOrUpdate(record);
        UpdateSearchResults();

        return BookmarkTreeAddBookmarkResult.Added(
            bookmark,
            targetParent,
            targetIndex: 0);
    }

    public BookmarkTreeAddFolderResult AddFolderToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title,
        BookmarkIconSelection? iconSelection = null)
    {
        if (!TryPrepareIconSelection(iconSelection, out var iconAssetId, out var shouldSetIcon))
            return BookmarkTreeAddFolderResult.NotAdded(targetParent);

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

        if (shouldSetIcon)
            record = _treeStore.SetItemIconAsset(record.Id, iconAssetId);

        var folder = new BookmarkFolderViewModel(
            record.Title ?? string.Empty,
            isExpanded: false,
            isRoot: false,
            id: record.Id,
            iconImage: _iconImageCache.GetImage(record.IconAssetId))
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
        string url,
        BookmarkIconSelection? iconSelection = null)
    {
        if (!TryPrepareIconSelection(iconSelection, out var iconAssetId, out var shouldSetIcon))
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);

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

        if (shouldSetIcon)
            record = _treeStore.SetItemIconAsset(bookmark.Id, iconAssetId);

        var oldTitle = bookmark.Title;
        var oldUrl = bookmark.Url;

        bookmark.SetTitle(record.Title ?? string.Empty);
        bookmark.SetUrl(record.Url ?? string.Empty);
        bookmark.SetIconImage(_iconImageCache.GetImage(record.IconAssetId));
        _searchService.AddOrUpdate(record);
        UpdateSearchResults();

        return BookmarkTreeEditBookmarkResult.Edited(
            bookmark,
            oldTitle,
            oldUrl);
    }

    public BookmarkTreeEditFolderResult EditFolder(
        BookmarkFolderViewModel folder,
        string title,
        BookmarkIconSelection? iconSelection = null)
    {
        if (folder.IsRoot)
            return BookmarkTreeEditFolderResult.NotEdited(folder);

        if (!TryPrepareIconSelection(iconSelection, out var iconAssetId, out var shouldSetIcon))
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

        if (shouldSetIcon)
            record = _treeStore.SetItemIconAsset(folder.Id, iconAssetId);

        var oldTitle = folder.Title;
        folder.SetTitle(record.Title ?? string.Empty);
        folder.SetIconImage(_iconImageCache.GetImage(record.IconAssetId));

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

        if (item is BookmarkViewModel)
        {
            _searchService.Remove(item.Id);
            _bookmarkViewModelsById.Remove(item.Id);
        }
        else
        {
            _searchService.Rebuild(_treeStore.Load());
            RebuildBookmarkLookup();
        }

        UpdateSearchResults();

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

    private void UpdateSearchResults()
    {
        SearchResults.Clear();

        if (IsSearchActive)
        {
            foreach (var result in _searchService.Search(SearchQuery, new BookmarkSearchOptions { MaxResults = 20 }))
            {
                if (!_bookmarkViewModelsById.TryGetValue(result.Id, out var bookmark))
                    continue;

                SearchResults.Add(new BookmarkSearchResultItem(
                    bookmark.Id,
                    bookmark.Title,
                    bookmark.Url,
                    bookmark.IconImage));
            }
        }

        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(IsBookmarksTreeVisible));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private void RebuildBookmarkLookup()
    {
        _bookmarkViewModelsById.Clear();

        foreach (var bookmark in EnumerateItems(Items).OfType<BookmarkViewModel>())
            _bookmarkViewModelsById[bookmark.Id] = bookmark;
    }

    private static string? GetStorageParentId(BookmarkFolderViewModel? targetParent)
    {
        return targetParent is null || targetParent.IsRoot ? null : targetParent.Id;
    }

    private bool TryPrepareIconSelection(
        BookmarkIconSelection? iconSelection,
        out string? iconAssetId,
        out bool shouldSetIcon)
    {
        iconAssetId = null;
        shouldSetIcon = false;

        if (iconSelection is null || iconSelection.Kind == BookmarkIconSelectionKind.KeepExisting)
            return true;

        if (iconSelection.Kind == BookmarkIconSelectionKind.UseDefault)
        {
            shouldSetIcon = true;
            return true;
        }

        try
        {
            var iconAsset = _iconAssetService.GetOrCreateFromOriginalBytes(iconSelection.OriginalBytes);
            iconAssetId = iconAsset.Id;
            shouldSetIcon = true;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    private string _title;
    private bool _isDragSource;
    private bool _isDragDimmed;
    private IImage? _iconImage;

    protected BookmarkTreeItemViewModel(string title, string? id = null, IImage? iconImage = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        _title = title;
        _iconImage = iconImage;
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public BookmarkFolderViewModel? Parent { get; internal set; }

    public IImage? IconImage
    {
        get => _iconImage;
        private set
        {
            if (!SetProperty(ref _iconImage, value))
                return;

            OnPropertyChanged(nameof(HasCustomIcon));
            OnPropertyChanged(nameof(HasDefaultIcon));
        }
    }

    public bool HasCustomIcon => IconImage is not null;

    public bool HasDefaultIcon => IconImage is null;

    internal void SetTitle(string title)
    {
        Title = title;
    }

    internal void SetIconImage(IImage? iconImage)
    {
        IconImage = iconImage;
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
        string? id = null,
        IImage? iconImage = null)
        : base(title, id, iconImage)
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

    public BookmarkViewModel(string title, string url, string? id = null, IImage? iconImage = null)
        : base(title, id, iconImage)
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
