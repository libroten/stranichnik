using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Media;
using Stranichnik.Diagnostics;
using Stranichnik.Icons;
using Stranichnik.Localization;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Security;
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
    private readonly SecretIconAssetService _secretIconAssetService;
    private readonly IconLibraryService _iconLibraryService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The view model intentionally depends on the crypto abstraction so secret behavior can be tested independently.")]
    private readonly ISecretCryptoService _secretCryptoService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The view model intentionally depends on the profile store abstraction so SQLite and tests can use different implementations.")]
    private readonly ISecretProfileStore _secretProfileStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The view model intentionally depends on the secret session abstraction so session behavior can be tested independently.")]
    private readonly ISecretSessionService _secretSession;
    private readonly SecretBookmarkProjectionService _secretProjectionService;
    private readonly SecretProfileSetupService _secretProfileSetupService;
    private readonly SecretUnlockService _secretUnlockService;
    private readonly SecretMasterPasswordChangeService _secretMasterPasswordChangeService;
    private readonly SecretMasterPasswordResetService? _secretMasterPasswordResetService;
    private readonly Dictionary<string, BookmarkViewModel> _bookmarkViewModelsById = new(StringComparer.Ordinal);
    private string _searchQuery = string.Empty;

    public MainWindowViewModel(
        IBookmarkTreeStore treeStore,
        BookmarkSearchService searchService,
        BookmarkIconImageCache? iconImageCache = null,
        IReadOnlySet<string>? expandedFolderIds = null,
        ISecretProfileStore? secretProfileStore = null,
        ISecretCryptoService? secretCryptoService = null,
        ISecretSessionService? secretSession = null,
        SecretBookmarkProjectionService? secretProjectionService = null,
        ISecretResetStore? secretResetStore = null)
    {
        ArgumentNullException.ThrowIfNull(treeStore);
        ArgumentNullException.ThrowIfNull(searchService);

        _treeStore = treeStore;
        _searchService = searchService;
        _secretProfileStore = secretProfileStore ?? new InMemorySecretProfileStore();
        _secretCryptoService = secretCryptoService ?? new SecretCryptoService();
        _secretSession = secretSession ?? new SecretSessionService();
        _secretIconAssetService = new SecretIconAssetService(_treeStore, new IconImageProcessor());
        _iconImageCache = iconImageCache ?? new BookmarkIconImageCache(
            treeStore,
            _secretSession);
        _iconAssetService = new IconAssetService(_treeStore, new IconImageProcessor());
        _iconLibraryService = new IconLibraryService(_treeStore, _iconImageCache);
        _secretProjectionService = secretProjectionService ?? new SecretBookmarkProjectionService(_secretCryptoService);
        _secretProfileSetupService = new SecretProfileSetupService(
            _secretProfileStore,
            _secretCryptoService,
            _secretSession);
        _secretUnlockService = new SecretUnlockService(
            _secretProfileStore,
            _secretCryptoService,
            _secretSession);
        _secretMasterPasswordChangeService = new SecretMasterPasswordChangeService(
            _secretProfileStore,
            _secretCryptoService,
            _secretSession);
        secretResetStore ??= CreateDefaultSecretResetStore(treeStore, _secretProfileStore);
        if (secretResetStore is not null)
        {
            _secretMasterPasswordResetService = new SecretMasterPasswordResetService(
                _secretProfileStore,
                secretResetStore,
                _secretSession);
        }

        _secretSession.StateChanged += OnSecretSessionStateChanged;

        RootFolder = new BookmarkFolderViewModel(
            UiStrings.RootAllBookmarks,
            children: null,
            isExpanded: true,
            isRoot: true);

        ReloadVisibleTreeAndSearch(expandedFolderIds);
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

    public bool IsSecretProfileConfigured => _secretProfileStore.LoadActiveProfile() is not null;

    public bool IsSecretSessionUnlocked => _secretSession.IsUnlocked;

    public bool AreSecretsVisible => _secretSession.AreSecretsVisible;

    public IReadOnlyList<BookmarkSearchResult> SearchBookmarks(
        string query,
        BookmarkSearchOptions? options = null)
    {
        return _searchService.Search(query, options);
    }

    public List<IconLibraryItem> BuildIconLibrary(IconLibraryRequest request)
    {
        var projected = _secretProjectionService.Project(_treeStore.Load(), _secretSession);
        LogProjectionWarnings(projected);
        return _iconLibraryService.Build(request, projected.Snapshot, _secretSession.AreSecretsVisible);
    }

    public BookmarkFolderViewModel? FindFolder(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (RootFolder.Id == id)
            return RootFolder;

        return EnumerateFolders(RootFolder.Children)
            .FirstOrDefault(folder => folder.Id == id);
    }

    public BookmarkViewModel? FindBookmark(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return EnumerateItems(Items)
            .OfType<BookmarkViewModel>()
            .FirstOrDefault(bookmark => bookmark.Id == id);
    }

    public void ReloadVisibleTreeAndSearch()
    {
        ReloadVisibleTreeAndSearch(captureExpandedFolderIds: true);
    }

    public void RefreshSecretSessionConfigurationFromStorage()
    {
        var hasSecretProfile = _secretProfileStore.LoadActiveProfile() is not null;

        if (hasSecretProfile && !_secretSession.IsConfigured)
        {
            _secretSession.MarkConfiguredLocked();
            Logs.Print("Secret session marked configured after sync storage refresh.");
        }
        else if (!hasSecretProfile && _secretSession.IsConfigured)
        {
            _secretSession.MarkNotConfigured();
            Logs.Print("Secret session marked not configured after sync storage refresh.");
        }

        OnPropertyChanged(nameof(IsSecretProfileConfigured));
        OnPropertyChanged(nameof(IsSecretSessionUnlocked));
        OnPropertyChanged(nameof(AreSecretsVisible));
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    public SecretProfileSetupResult CreateMasterPassword(
        string masterPassword,
        bool showSecrets)
    {
        var result = _secretProfileSetupService.CreateMasterPassword(masterPassword, showSecrets);
        OnPropertyChanged(nameof(IsSecretProfileConfigured));
        OnPropertyChanged(nameof(IsSecretSessionUnlocked));
        OnPropertyChanged(nameof(AreSecretsVisible));
        return result;
    }

    public SecretSessionUnlockResult UnlockSecrets(
        string masterPassword,
        bool showSecrets)
    {
        var result = _secretUnlockService.Unlock(masterPassword, showSecrets);
        OnPropertyChanged(nameof(IsSecretSessionUnlocked));
        OnPropertyChanged(nameof(AreSecretsVisible));
        return result;
    }

    public bool ShowSecrets()
    {
        if (!_secretSession.IsUnlocked)
            return false;

        try
        {
            _secretSession.ShowSecrets();
            Logs.Print("Secrets shown.");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void HideSecretsByUserAction()
    {
        var wereVisible = _secretSession.AreSecretsVisible;
        _secretSession.HideSecrets();

        if (wereVisible)
            Logs.Print("Secrets hidden by user action.");
    }

    public void HideSecretsByInactivityTimeout()
    {
        var wereVisible = _secretSession.AreSecretsVisible;
        _secretSession.HideSecrets();

        if (wereVisible)
            Logs.Print("Secrets hidden by inactivity timeout.");
    }

    public SecretMasterPasswordChangeResult ChangeMasterPassword(string newMasterPassword)
    {
        return _secretMasterPasswordChangeService.ChangeMasterPassword(newMasterPassword);
    }

    public SecretPasswordSaveResult SaveSecretMasterPassword(string newMasterPassword)
    {
        if (!IsSecretProfileConfigured)
        {
            var setupResult = CreateMasterPassword(newMasterPassword, showSecrets: false);
            return setupResult.WasCreated
                ? SecretPasswordSaveResult.Created()
                : SecretPasswordSaveResult.Failed(SecretPasswordSaveFailureReason.SetupFailed);
        }

        if (!IsSecretSessionUnlocked)
            return SecretPasswordSaveResult.Failed(SecretPasswordSaveFailureReason.UnlockRequired);

        var changeResult = ChangeMasterPassword(newMasterPassword);
        return changeResult.WasChanged
            ? SecretPasswordSaveResult.Changed()
            : SecretPasswordSaveResult.Failed(SecretPasswordSaveFailureReason.ChangeFailed);
    }

    public SecretMasterPasswordResetResult ResetMasterPasswordAndDeleteSecrets()
    {
        if (_secretMasterPasswordResetService is null)
        {
            Logs.Print("Secret master password reset failed. Reason=StoreResetFailed.");
            return SecretMasterPasswordResetResult.Failed(
                SecretMasterPasswordResetFailureReason.StoreResetFailed);
        }

        var result = _secretMasterPasswordResetService.ResetMasterPasswordAndDeleteSecrets();
        if (!result.WasReset)
            return result;

        OnPropertyChanged(nameof(IsSecretProfileConfigured));
        return result;
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
        targetParent = RefreshTargetFolderReference(targetParent, "AddBookmark");

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

    public BookmarkTreeAddBookmarkResult AddSecretBookmarkToFolderStart(
        BookmarkFolderViewModel targetParent,
        string title,
        string url,
        BookmarkIconSelection? iconSelection = null)
    {
        targetParent = RefreshTargetFolderReference(targetParent, "AddSecretBookmark");

        if (!TryPrepareSecretIconSelection(iconSelection, currentRecord: null, out var secretIconAssetId, out var shouldSetIcon))
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);

        var bookmarkId = Guid.NewGuid().ToString("N");

        if (!TryCreateEncryptedBookmarkPayload(bookmarkId, title, url, out var encryptedPayload))
            return BookmarkTreeAddBookmarkResult.NotAdded(targetParent);

        BookmarkItemRecord record;

        try
        {
            record = _treeStore.AddSecretBookmarkToFolderStart(
                GetStorageParentId(targetParent),
                bookmarkId,
                encryptedPayload);
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
            record = _treeStore.SetItemSecretIconAsset(record.Id, secretIconAssetId);

        var projectedRecord = record with
        {
            Title = title,
            Url = url
        };
        var bookmark = new BookmarkViewModel(
            title,
            url,
            record.Id,
            _iconImageCache.GetSecretImage(record.SecretIconAssetId),
            isSecret: true)
        {
            Parent = targetParent
        };

        targetParent.Children.Insert(0, bookmark);
        _bookmarkViewModelsById[bookmark.Id] = bookmark;
        _searchService.AddOrUpdate(projectedRecord);
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
        targetParent = RefreshTargetFolderReference(targetParent, "AddFolder");

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

    public BookmarkTreeEditBookmarkResult EditBookmarkAsSecret(
        BookmarkViewModel bookmark,
        string title,
        string url,
        BookmarkIconSelection? iconSelection = null)
    {
        var currentRecord = FindStorageRecord(bookmark.Id);
        if (!TryPrepareSecretIconSelection(iconSelection, currentRecord, out var secretIconAssetId, out var shouldSetIcon))
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);

        if (!TryCreateEncryptedBookmarkPayload(bookmark.Id, title, url, out var encryptedPayload))
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);

        BookmarkItemRecord record;

        try
        {
            record = _treeStore.EditBookmarkAsSecret(bookmark.Id, encryptedPayload);
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
            record = _treeStore.SetItemSecretIconAsset(bookmark.Id, secretIconAssetId);

        var oldTitle = bookmark.Title;
        var oldUrl = bookmark.Url;

        bookmark.SetTitle(title);
        bookmark.SetUrl(url);
        bookmark.SetIsSecret(true);
        bookmark.SetIconImage(_iconImageCache.GetSecretImage(record.SecretIconAssetId));
        _searchService.AddOrUpdate(record with
        {
            Title = title,
            Url = url
        });
        UpdateSearchResults();

        return BookmarkTreeEditBookmarkResult.Edited(
            bookmark,
            oldTitle,
            oldUrl);
    }

    public BookmarkTreeEditBookmarkResult EditSecretBookmarkAsPlaintext(
        BookmarkViewModel bookmark,
        string title,
        string url,
        BookmarkIconSelection? iconSelection = null)
    {
        var currentRecord = FindStorageRecord(bookmark.Id);
        if (!TryPreparePlainIconSelection(iconSelection, currentRecord, out var iconAssetId, out var shouldSetIcon))
            return BookmarkTreeEditBookmarkResult.NotEdited(bookmark);

        BookmarkItemRecord record;

        try
        {
            record = _treeStore.EditSecretBookmarkAsPlaintext(bookmark.Id, title, url);
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
        bookmark.SetIsSecret(false);
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
            RebuildSearchFromVisibleSnapshot();
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

    private void ReloadVisibleTreeAndSearch(IReadOnlySet<string>? expandedFolderIds)
    {
        var snapshot = _treeStore.Load();
        var projected = _secretProjectionService.Project(snapshot, _secretSession);
        LogProjectionWarnings(projected);
        var items = BookmarkTreeViewModelMapper.CreateViewModels(
            projected.Snapshot,
            expandedFolderIds,
            _iconImageCache);

        ReplaceRootChildren(items);
        _searchService.Rebuild(projected.Snapshot);
        RebuildBookmarkLookup();
        UpdateSearchResults();
    }

    private void ReloadVisibleTreeAndSearch(bool captureExpandedFolderIds)
    {
        var expandedFolderIds = captureExpandedFolderIds
            ? CaptureExpandedFolderIds()
            : null;

        ReloadVisibleTreeAndSearch(expandedFolderIds);
    }

    private void RebuildSearchFromVisibleSnapshot()
    {
        var projected = _secretProjectionService.Project(_treeStore.Load(), _secretSession);
        LogProjectionWarnings(projected);
        _searchService.Rebuild(projected.Snapshot);
    }

    private static void LogProjectionWarnings(SecretProjectionResult projected)
    {
        foreach (var group in projected.Warnings.GroupBy(warning => warning.Reason))
            Logs.Print($"Secret bookmark projection warning: Reason={group.Key}, Count={group.Count()}.");
    }

    private HashSet<string> CaptureExpandedFolderIds()
    {
        return EnumerateFoldersIncludingRoot()
            .Where(folder => folder is { IsRoot: false, IsExpanded: true })
            .Select(folder => folder.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void ReplaceRootChildren(IEnumerable<BookmarkTreeItemViewModel> items)
    {
        RootFolder.Children.Clear();

        foreach (var item in items)
        {
            item.Parent = RootFolder;
            RootFolder.Children.Add(item);
        }
    }

    private void OnSecretSessionStateChanged(object? sender, SecretSessionChangedEventArgs args)
    {
        _ = sender;
        _ = args;

        _iconImageCache.ClearSecretImages();
        ReloadVisibleTreeAndSearch();
        OnPropertyChanged(nameof(IsSecretSessionUnlocked));
        OnPropertyChanged(nameof(AreSecretsVisible));
    }

    private static string? GetStorageParentId(BookmarkFolderViewModel? targetParent)
    {
        return targetParent is null || targetParent.IsRoot ? null : targetParent.Id;
    }

    private static InMemorySecretResetStore? CreateDefaultSecretResetStore(
        IBookmarkTreeStore treeStore,
        ISecretProfileStore secretProfileStore)
    {
        return treeStore is InMemoryBookmarkTreeStore inMemoryTreeStore &&
            secretProfileStore is InMemorySecretProfileStore inMemorySecretProfileStore
            ? new InMemorySecretResetStore(inMemoryTreeStore, inMemorySecretProfileStore)
            : null;
    }

    private BookmarkFolderViewModel RefreshTargetFolderReference(
        BookmarkFolderViewModel targetParent,
        string operationName)
    {
        var currentTargetParent = FindFolder(targetParent.Id);
        if (currentTargetParent is null || ReferenceEquals(currentTargetParent, targetParent))
            return targetParent;

        Logs.Print($"Bookmark tree operation target folder refreshed after tree reload. Operation={operationName}.");
        return currentTargetParent;
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

        if (iconSelection.Kind == BookmarkIconSelectionKind.UseLibraryIcon)
        {
            return TryPrepareRegularLibraryIconSelection(
                iconSelection.LibrarySelection,
                out iconAssetId,
                out shouldSetIcon);
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

    private bool TryPreparePlainIconSelection(
        BookmarkIconSelection? iconSelection,
        BookmarkItemRecord? currentRecord,
        out string? iconAssetId,
        out bool shouldSetIcon)
    {
        iconAssetId = null;
        shouldSetIcon = false;

        if (iconSelection is null || iconSelection.Kind == BookmarkIconSelectionKind.KeepExisting)
        {
            if (currentRecord?.SecretIconAssetId is null)
                return true;

            return TryConvertSecretIconToPlain(currentRecord.SecretIconAssetId, out iconAssetId, out shouldSetIcon);
        }

        return TryPrepareIconSelection(iconSelection, out iconAssetId, out shouldSetIcon);
    }

    private bool TryPrepareSecretIconSelection(
        BookmarkIconSelection? iconSelection,
        BookmarkItemRecord? currentRecord,
        out string? secretIconAssetId,
        out bool shouldSetIcon)
    {
        secretIconAssetId = null;
        shouldSetIcon = false;

        if (iconSelection is null || iconSelection.Kind == BookmarkIconSelectionKind.KeepExisting)
        {
            if (currentRecord?.IconAssetId is not null)
                return TryConvertPlainIconToSecret(currentRecord.IconAssetId, out secretIconAssetId, out shouldSetIcon);

            return true;
        }

        if (iconSelection.Kind == BookmarkIconSelectionKind.UseDefault)
        {
            shouldSetIcon = true;
            return true;
        }

        if (iconSelection.Kind == BookmarkIconSelectionKind.UseLibraryIcon)
        {
            return TryPrepareSecretLibraryIconSelection(
                iconSelection.LibrarySelection,
                out secretIconAssetId,
                out shouldSetIcon);
        }

        var profile = _secretProfileStore.LoadActiveProfile();
        var dataKey = _secretSession.BorrowDataKey();

        if (profile is null || dataKey is null)
            return false;

        try
        {
            var iconAsset = _secretIconAssetService.GetOrCreateFromOriginalBytes(
                iconSelection.OriginalBytes,
                dataKey,
                profile.SecretGenerationId);
            secretIconAssetId = iconAsset.Id;
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
        catch (SecretPayloadException)
        {
            return false;
        }
    }

    private bool TryPrepareRegularLibraryIconSelection(
        IconLibrarySelection? librarySelection,
        out string? iconAssetId,
        out bool shouldSetIcon)
    {
        iconAssetId = null;
        shouldSetIcon = false;

        if (librarySelection is null)
            return false;

        if (librarySelection.RegularIconAssetId is not null)
        {
            if (_treeStore.GetIconAsset(librarySelection.RegularIconAssetId) is null)
            {
                Logs.Print("Regular icon library selection failed: regular asset was not found.");
                return false;
            }

            Logs.Print("Regular icon library selection applied directly.");
            iconAssetId = librarySelection.RegularIconAssetId;
            shouldSetIcon = true;
            return true;
        }

        if (librarySelection.SecretIconAssetId is null)
        {
            Logs.Print("Regular icon library selection failed: no compatible asset reference.");
            return false;
        }

        Logs.Print("Regular icon library selection requires secret-to-regular icon conversion.");
        return TryConvertSecretIconToPlain(
            librarySelection.SecretIconAssetId,
            out iconAssetId,
            out shouldSetIcon);
    }

    private bool TryPrepareSecretLibraryIconSelection(
        IconLibrarySelection? librarySelection,
        out string? secretIconAssetId,
        out bool shouldSetIcon)
    {
        secretIconAssetId = null;
        shouldSetIcon = false;

        if (librarySelection is null)
            return false;

        if (librarySelection.SecretIconAssetId is not null)
        {
            if (_treeStore.GetSecretIconAsset(librarySelection.SecretIconAssetId) is null)
            {
                Logs.Print("Secret icon library selection failed: secret asset was not found.");
                return false;
            }

            Logs.Print("Secret icon library selection applied directly.");
            secretIconAssetId = librarySelection.SecretIconAssetId;
            shouldSetIcon = true;
            return true;
        }

        if (librarySelection.RegularIconAssetId is null)
        {
            Logs.Print("Secret icon library selection failed: no compatible asset reference.");
            return false;
        }

        Logs.Print("Secret icon library selection requires regular-to-secret icon conversion.");
        return TryConvertPlainIconToSecret(
            librarySelection.RegularIconAssetId,
            out secretIconAssetId,
            out shouldSetIcon);
    }

    private bool TryConvertPlainIconToSecret(
        string iconAssetId,
        out string? secretIconAssetId,
        out bool shouldSetIcon)
    {
        secretIconAssetId = null;
        shouldSetIcon = false;
        var profile = _secretProfileStore.LoadActiveProfile();
        var dataKey = _secretSession.BorrowDataKey();

        if (profile is null || dataKey is null)
        {
            Logs.Print("Regular-to-secret icon conversion failed: secret runtime key unavailable.");
            return false;
        }

        try
        {
            var iconAsset = _secretIconAssetService.GetOrCreateFromRegularIconAsset(
                iconAssetId,
                dataKey,
                profile.SecretGenerationId);
            if (iconAsset is null)
            {
                Logs.Print("Regular-to-secret icon conversion failed: source asset was not found.");
                return false;
            }

            secretIconAssetId = iconAsset.Id;
            shouldSetIcon = true;
            return true;
        }
        catch (ArgumentException)
        {
            Logs.Print("Regular-to-secret icon conversion failed: invalid asset data.");
            return false;
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Regular-to-secret icon conversion failed: storage operation rejected.");
            return false;
        }
        catch (SecretPayloadException)
        {
            Logs.Print("Regular-to-secret icon conversion failed: secret payload error.");
            return false;
        }
    }

    private bool TryConvertSecretIconToPlain(
        string secretIconAssetId,
        out string? iconAssetId,
        out bool shouldSetIcon)
    {
        iconAssetId = null;
        shouldSetIcon = false;
        var dataKey = _secretSession.BorrowDataKey();

        if (dataKey is null)
        {
            Logs.Print("Secret-to-regular icon conversion failed: secret runtime key unavailable.");
            return false;
        }

        try
        {
            var iconAsset = _secretIconAssetService.GetOrCreateRegularFromSecretIconAsset(
                secretIconAssetId,
                dataKey);
            if (iconAsset is null)
            {
                Logs.Print("Secret-to-regular icon conversion failed: source asset was not found.");
                return false;
            }

            iconAssetId = iconAsset.Id;
            shouldSetIcon = true;
            return true;
        }
        catch (ArgumentException)
        {
            Logs.Print("Secret-to-regular icon conversion failed: invalid asset data.");
            return false;
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Secret-to-regular icon conversion failed: storage operation rejected.");
            return false;
        }
        catch (SecretPayloadException)
        {
            Logs.Print("Secret-to-regular icon conversion failed: secret payload error.");
            return false;
        }
    }

    private BookmarkItemRecord? FindStorageRecord(string itemId)
    {
        return _treeStore.Load()
            .Items
            .FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
    }

    private bool TryCreateEncryptedBookmarkPayload(
        string bookmarkId,
        string title,
        string url,
        out EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        encryptedPayload = null!;
        var profile = _secretProfileStore.LoadActiveProfile();
        var dataKey = _secretSession.BorrowDataKey();

        if (profile is null || dataKey is null)
            return false;

        try
        {
            var encrypted = _secretCryptoService.EncryptBookmarkPayload(
                new SecretBookmarkPayloadV1(title, url),
                dataKey,
                profile.Id,
                bookmarkId);
            encryptedPayload = new EncryptedBookmarkPayloadRecord(
                encrypted.Payload,
                encrypted.Nonce,
                encrypted.CryptoProfileId,
                encrypted.PayloadFormatVersion);
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
        catch (SecretPayloadException)
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
        set => SetProperty(ref _isExpanded, value);
    }

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
    private bool _isSecret;

    public BookmarkViewModel(
        string title,
        string url,
        string? id = null,
        IImage? iconImage = null,
        bool isSecret = false)
        : base(title, id, iconImage)
    {
        _url = url;
        _isSecret = isSecret;
    }

    public string Url
    {
        get => _url;
        private set => SetProperty(ref _url, value);
    }

    public bool IsSecret
    {
        get => _isSecret;
        private set => SetProperty(ref _isSecret, value);
    }

    internal void SetUrl(string url)
    {
        Url = url;
    }

    internal void SetIsSecret(bool isSecret)
    {
        IsSecret = isSecret;
    }
}
