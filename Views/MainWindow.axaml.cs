using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stranichnik.Diagnostics;
using Stranichnik.Icons;
using Stranichnik.Localization;
using Stranichnik.Opening;
using Stranichnik.Searching;
using Stranichnik.Security;
using Stranichnik.Settings;
using Stranichnik.Sync;
using Stranichnik.Sync.Credentials;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.RemoteProblems;
using Stranichnik.Theming;
using Stranichnik.ViewModels;

namespace Stranichnik.Views;

public partial class MainWindow : Window
{
    private const double TreeIndentWidth = 28;
    private const double OverflowRowWidth = 560;
    private const double DragStartThreshold = 6;
    private const double DragAutoScrollEdgeSize = 56;
    private const double DragAutoScrollMaxStep = 18;
    private const double DragGhostOffsetX = 10;
    private const double DragGhostOpacity = 0.92;
    private const double DragGhostInitialScale = 0.85;
    private static readonly TimeSpan FolderAutoExpandDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan DragAutoScrollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan DragGhostAnimationDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SyncProgressAnimationInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SyncProgressAnimationHalfCycle = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan SyncProgressCompletionHoldDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SecretInactivityTimeout = TimeSpan.FromMinutes(1);

    private bool _isMiddleButtonPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
    private BookmarkTreeItemViewModel? _contextMenuItem;
    private BookmarkTreeItemViewModel? _pressedTreeItem;
    private BookmarkTreeItemViewModel? _pressedFolderClickCandidate;
    private BookmarkTreeItemViewModel? _draggedTreeItem;
    private BookmarkFolderViewModel? _activeDropTargetFolder;
    private BookmarkFolderViewModel? _pendingAutoExpandFolder;
    private Point _treeDragStartPoint;
    private Point _lastTreeDragPoint;
    private double _dragStartWindowY;
    private bool _isTreeDragging;
    private bool _hasActiveDropTarget;
    private bool _hasDragStartWindowY;
    private readonly DispatcherTimer _folderAutoExpandTimer;
    private readonly DispatcherTimer _dragAutoScrollTimer;
    private readonly DispatcherTimer _dragGhostAnimationTimer;
    private readonly DispatcherTimer _syncProgressAnimationTimer;
    private readonly SecretInactivityController _secretInactivityController;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The window intentionally depends on the credential abstraction so tests and future credential stores can share the same UI wiring.")]
    private readonly ISyncCredentialStore _syncCredentialStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The window intentionally depends on the sync storage abstraction so the UI does not know the persistence implementation.")]
    private readonly ISyncLocalStore? _syncLocalStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The window intentionally depends on the operation gate abstraction so sync coordination remains replaceable.")]
    private readonly ISyncOperationGate _syncOperationGate;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The window intentionally depends on the sync activity abstraction so UI activity reporting stays decoupled from sync implementation details.")]
    private readonly ISyncActivityService _syncActivityService;
    private readonly ScaleTransform _dragGhostScaleTransform = new()
    {
        ScaleX = 1,
        ScaleY = 1
    };
    private DateTimeOffset _dragGhostAnimationStartedAt;
    private DateTimeOffset _syncProgressAnimationStartedAt;
    private bool? _pendingSyncProgressCompletionSucceeded;
    private bool _isSyncProgressCompletionVisible;
    private int _syncProgressCompletionGeneration;
    private bool _isManualSyncRunning;
    private SettingsDialogResult? _lastManualSyncResult;
    private MainWindowViewModel? _observedViewModel;

    public MainWindow()
        : this(SyncCredentialStoreFactory.CreateDefault(), syncLocalStore: null, new SyncOperationGate(), new SyncActivityService())
    {
    }

    public MainWindow(
        ISyncCredentialStore syncCredentialStore,
        ISyncLocalStore? syncLocalStore,
        ISyncOperationGate syncOperationGate,
        ISyncActivityService syncActivityService)
    {
        ArgumentNullException.ThrowIfNull(syncCredentialStore);
        ArgumentNullException.ThrowIfNull(syncOperationGate);
        ArgumentNullException.ThrowIfNull(syncActivityService);

        _syncCredentialStore = syncCredentialStore;
        _syncLocalStore = syncLocalStore;
        _syncOperationGate = syncOperationGate;
        _syncActivityService = syncActivityService;

        InitializeComponent();

        _folderAutoExpandTimer = new DispatcherTimer
        {
            Interval = FolderAutoExpandDelay
        };
        _folderAutoExpandTimer.Tick += OnFolderAutoExpandTimerTick;

        _dragAutoScrollTimer = new DispatcherTimer
        {
            Interval = DragAutoScrollInterval
        };
        _dragAutoScrollTimer.Tick += OnDragAutoScrollTimerTick;

        _dragGhostAnimationTimer = new DispatcherTimer
        {
            Interval = DragAutoScrollInterval
        };
        _dragGhostAnimationTimer.Tick += OnDragGhostAnimationTimerTick;
        DragGhost.RenderTransform = _dragGhostScaleTransform;
        DragGhost.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        _syncProgressAnimationTimer = new DispatcherTimer
        {
            Interval = SyncProgressAnimationInterval
        };
        _syncProgressAnimationTimer.Tick += OnSyncProgressAnimationTimerTick;

        var secretInactivityTimer = new DispatcherTimer
        {
            Interval = SecretInactivityTimeout
        };
        _secretInactivityController = new SecretInactivityController(
            new DispatcherSecretInactivityTimer(secretInactivityTimer),
            OnSecretInactivityTimeout);

        AddHandler(PointerPressedEvent, OnWindowActivityPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowActivityPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnWindowActivityPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        BookmarksScrollViewer.SizeChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        _syncActivityService.ActivityChanged += OnSyncActivityChanged;
        _syncActivityService.ActivityCompleted += OnSyncActivityCompleted;
        SetSyncProgressActive(_syncActivityService.IsActive);
        UpdateBookmarksHorizontalOverflow();
    }

    private void OnStranichnikMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        ToggleMenuPopup(StranichnikMenuPopup, StranichnikMenuButton);
        e.Handled = true;
    }

    private void OnServiceMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        ToggleMenuPopup(ServiceMenuPopup, ServiceMenuButton);
        UpdateSecretVisibilityMenuState();
        e.Handled = true;
    }

    private void OnStranichnikMenuPointerEntered(object? sender, PointerEventArgs e)
    {
        if (ServiceMenuPopup.IsOpen)
            OpenOnlyMenuPopup(StranichnikMenuPopup, StranichnikMenuButton);
    }

    private void OnServiceMenuPointerEntered(object? sender, PointerEventArgs e)
    {
        if (StranichnikMenuPopup.IsOpen)
        {
            OpenOnlyMenuPopup(ServiceMenuPopup, ServiceMenuButton);
            UpdateSecretVisibilityMenuState();
        }
    }

    private void OnAboutMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        CloseMenuPopups();
        e.Handled = true;

        _ = AboutDialog.ShowAsync(this);
    }

    private void OnExitMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        CloseMenuPopups();
        e.Handled = true;

        Close();
    }

    private void OnAppearanceMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        CloseMenuPopups();
        e.Handled = true;

        var settings = AppSettingsService.Load();
        var currentTheme = ThemeService.NormalizeTheme(settings.Theme);
        AppearanceDialog.Open(this, currentTheme, selectedTheme =>
        {
            var updatedSettings = AppSettingsService.Load();
            updatedSettings.Theme = ThemeService.ToSettingsValue(selectedTheme);
            AppSettingsService.Save(updatedSettings);
            ThemeService.Apply(selectedTheme);
        });
    }

    private void OnLanguageMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        CloseMenuPopups();
        e.Handled = true;

        var settings = AppSettingsService.Load();
        var currentLanguage = string.IsNullOrWhiteSpace(settings.Language)
            ? LanguageService.CurrentLanguage
            : settings.Language;
        LanguageDialog.Open(this, currentLanguage, async selectedLanguage =>
        {
            var updatedSettings = AppSettingsService.Load();
            updatedSettings.Language = selectedLanguage;
            AppSettingsService.Save(updatedSettings);
            LanguageService.Apply(selectedLanguage);

            await MessageDialog.ShowMessage(
                this,
                UiStrings.LanguageDialogRestartTitle,
                UiStrings.LanguageDialogRestartMessage);
        });
    }

    private void OnSettingsMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        CloseMenuPopups();
        e.Handled = true;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        SettingsDialog.Open(
            this,
            (owner, newMasterPassword) =>
                SaveSecretMasterPasswordFromSettingsAsync(owner, viewModel, newMasterPassword),
            _ => ResetSecretMasterPasswordFromSettingsAsync(viewModel),
            _ => TestSyncConnectionFromSettingsAsync(_syncCredentialStore),
            _ => SyncNowFromSettingsAsync(viewModel),
            LoadSyncRemoteProblemsForSettings,
            (_, problemId) => ClearSyncRemoteProblemFromSettingsAsync(problemId),
            (_, problemId) => DeleteSyncRemoteProblemFromSettingsAsync(problemId),
            _syncCredentialStore,
            _syncActivityService,
            _lastManualSyncResult);
    }

    private async void OnToggleSecretsMenuClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        e.Handled = true;

        await ToggleSecretBookmarksAsync(closePopups: false);
        UpdateSecretVisibilityMenuState();
    }

    private async Task<SettingsDialogResult> SaveSecretMasterPasswordFromSettingsAsync(
        Window owner,
        MainWindowViewModel viewModel,
        string newMasterPassword)
    {
        if (viewModel is { IsSecretProfileConfigured: true, IsSecretSessionUnlocked: false } &&
            !await ShowUnlockSecretsDialogAsync(owner, viewModel, showSecrets: false))
        {
            return SettingsDialogResult.Failed(UiStrings.SettingsSecretUnlockRequired);
        }

        SecretPasswordSaveResult result;
        using (_syncOperationGate.EnterLocalWriteOperation())
            result = viewModel.SaveSecretMasterPassword(newMasterPassword);

        if (result.Succeeded)
        {
            return result.WasCreated
                ? SettingsDialogResult.Created()
                : SettingsDialogResult.Changed();
        }

        return SettingsDialogResult.Failed(result.FailureReason switch
        {
            SecretPasswordSaveFailureReason.UnlockRequired => UiStrings.SettingsSecretUnlockRequired,
            SecretPasswordSaveFailureReason.SetupFailed => UiStrings.SecretSetupFailedMessage,
            _ => UiStrings.SettingsSecretPasswordSaveFailed
        });
    }

    private Task<SettingsDialogResult> ResetSecretMasterPasswordFromSettingsAsync(
        MainWindowViewModel viewModel)
    {
        SecretMasterPasswordResetResult result;
        using (_syncOperationGate.EnterLocalWriteOperation())
            result = viewModel.ResetMasterPasswordAndDeleteSecrets();

        if (result.WasReset)
            return Task.FromResult(SettingsDialogResult.Reset());

        var message = result.FailureReason switch
        {
            SecretMasterPasswordResetFailureReason.NotConfigured => UiStrings.SettingsSecretResetNotConfigured,
            _ => UiStrings.SettingsSecretResetFailed
        };

        return Task.FromResult(SettingsDialogResult.Failed(message));
    }

    private async Task<SettingsDialogResult> TestSyncConnectionFromSettingsAsync(
        ISyncCredentialStore syncCredentialStore)
    {
        var service = new SyncConnectionTestService(syncActivityService: _syncActivityService);
        var result = await service.TestAsync(
            AppSettingsService.Load(),
            syncCredentialStore,
            CancellationToken.None);

        return result.Status switch
        {
            SyncConnectionTestStatus.Succeeded => SettingsDialogResult.Changed(),
            SyncConnectionTestStatus.MissingWebDavUrl => SettingsDialogResult.Failed(UiStrings.SettingsSyncWebDavUrlRequired),
            SyncConnectionTestStatus.InvalidWebDavUrl => SettingsDialogResult.Failed(UiStrings.SettingsSyncWebDavUrlInvalid),
            SyncConnectionTestStatus.MissingUsername => SettingsDialogResult.Failed(UiStrings.SettingsSyncUsernameRequired),
            SyncConnectionTestStatus.MissingCredentials => SettingsDialogResult.Failed(UiStrings.SettingsSyncPasswordRequired),
            SyncConnectionTestStatus.WrongCredentials => SettingsDialogResult.Failed(UiStrings.SettingsSyncWrongCredentials),
            SyncConnectionTestStatus.RemoteUnavailable => SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteUnavailable),
            _ => SettingsDialogResult.Failed(UiStrings.SettingsSyncConnectionFailed)
        };
    }

    private async Task<SettingsDialogResult> SyncNowFromSettingsAsync(MainWindowViewModel viewModel)
    {
        var result = await TryRunManualSyncAndRememberAsync(viewModel, ignoreIfRunning: false);
        return result ?? SettingsDialogResult.Failed(UiStrings.SettingsSyncNowUnavailable);
    }

    private async Task RunManualSyncFromShortcutAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        Logs.Print("Manual sync shortcut requested.");

        if (!HasRunnableSyncConfiguration())
        {
            Logs.Print("Manual sync shortcut stopped: sync settings are incomplete.");
            await MessageDialog.ShowMessage(
                this,
                UiStrings.SettingsSyncShortcutNotConfiguredTitle,
                UiStrings.SettingsSyncShortcutNotConfiguredMessage);
            return;
        }

        await TryRunManualSyncAndRememberAsync(viewModel, ignoreIfRunning: true);
    }

    private bool HasRunnableSyncConfiguration()
    {
        var settings = AppSettingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.Sync.WebDavUrl) ||
            string.IsNullOrWhiteSpace(settings.Sync.Username))
        {
            return false;
        }

        if (!IsValidSyncWebDavUrl(settings.Sync.WebDavUrl))
            return false;

        return !string.IsNullOrWhiteSpace(settings.Sync.CredentialStorageKind) &&
            _syncCredentialStore.Load() is not null;
    }

    private async Task<SettingsDialogResult?> TryRunManualSyncAndRememberAsync(
        MainWindowViewModel viewModel,
        bool ignoreIfRunning)
    {
        if (_isManualSyncRunning || _syncActivityService.IsActive)
        {
            if (ignoreIfRunning)
                Logs.Print("Manual sync shortcut ignored: sync operation is already active.");

            return null;
        }

        _isManualSyncRunning = true;
        try
        {
            var result = await ExecuteManualSyncAsync(viewModel);
            _lastManualSyncResult = result;
            return result;
        }
        finally
        {
            _isManualSyncRunning = false;
        }
    }

    private async Task<SettingsDialogResult> ExecuteManualSyncAsync(MainWindowViewModel viewModel)
    {
        if (_syncLocalStore is null)
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncNowUnavailable);

        var settings = AppSettingsService.Load();
        var factoryResult = new SyncApplicationServiceFactory(syncActivityService: _syncActivityService).Create(
            settings,
            _syncCredentialStore,
            _syncLocalStore,
            _syncOperationGate);

        if (factoryResult.Status != SyncApplicationServiceFactoryStatus.Ready ||
            factoryResult.Service is null)
        {
            return SettingsDialogResult.Failed(ToSyncSettingsErrorMessage(factoryResult.Status));
        }

        using var syncService = factoryResult.Service;
        SyncRunSummary summary;
        try
        {
            summary = await syncService.SyncNowAsync(CancellationToken.None);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Logs.Print("Manual sync failed: credentials rejected.");
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncWrongCredentials);
        }
        catch (HttpRequestException)
        {
            Logs.Print("Manual sync failed: remote unavailable.");
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteUnavailable);
        }
        catch (TaskCanceledException)
        {
            Logs.Print("Manual sync failed: request timed out.");
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteUnavailable);
        }
        catch (Exception exception) when (IsRecoverableLocalSyncException(exception))
        {
            Logs.Print($"Manual sync failed: local sync operation failed. ExceptionType={exception.GetType().Name}.");
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncNowFailed);
        }

        viewModel.RefreshSecretSessionConfigurationFromStorage();
        viewModel.ReloadVisibleTreeAndSearch();

        if (!summary.Succeeded)
            return SettingsDialogResult.Failed(ToSyncSummaryErrorMessage(summary));

        var updatedSettings = AppSettingsService.Load();
        updatedSettings.Sync.LastSuccessfulSyncAtUtc = summary.FinishedAtUtc;
        AppSettingsService.Save(updatedSettings);
        return SettingsDialogResult.Changed();
    }

    private List<SettingsSyncRemoteProblemViewModel> LoadSyncRemoteProblemsForSettings()
    {
        if (_syncLocalStore is null)
            return [];

        return _syncLocalStore
            .LoadSnapshot()
            .QuarantinedRemoteObjects
            .Select(problem => new SettingsSyncRemoteProblemViewModel(
                problem.Id,
                problem.RelativePath,
                problem.ReasonCode,
                problem.SeenCount))
            .ToList();
    }

    private Task<SettingsDialogResult> ClearSyncRemoteProblemFromSettingsAsync(string problemId)
    {
        if (_syncLocalStore is null)
            return Task.FromResult(SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteProblemClearFailed));

        _syncLocalStore.ClearQuarantinedRemoteObject(problemId);
        Logs.Print("Sync remote problem cleared from settings.");
        return Task.FromResult(SettingsDialogResult.Changed());
    }

    private async Task<SettingsDialogResult> DeleteSyncRemoteProblemFromSettingsAsync(string problemId)
    {
        if (_syncLocalStore is null)
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteProblemDeleteFailed);

        var problem = _syncLocalStore
            .LoadSnapshot()
            .QuarantinedRemoteObjects
            .FirstOrDefault(candidate => string.Equals(candidate.Id, problemId, StringComparison.Ordinal));
        if (problem is null)
            return SettingsDialogResult.Changed();

        var service = SyncRemoteProblemService.TryCreate(
            AppSettingsService.Load(),
            _syncCredentialStore,
            _syncLocalStore,
            _syncActivityService);
        if (service is null)
            return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteProblemDeleteUnavailable);

        using (service)
        {
            try
            {
                var result = await service
                    .DeleteRemoteProblemAsync(problem, CancellationToken.None);

                return result.Status switch
                {
                    SyncRemoteProblemDeleteStatus.DeletedOrMissing => SettingsDialogResult.Changed(),
                    SyncRemoteProblemDeleteStatus.RemoteChanged => SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteProblemDeleteChanged),
                    _ => SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteProblemDeleteFailed)
                };
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Logs.Print("Sync remote problem delete failed: credentials rejected.");
                return SettingsDialogResult.Failed(UiStrings.SettingsSyncWrongCredentials);
            }
            catch (HttpRequestException)
            {
                Logs.Print("Sync remote problem delete failed: remote unavailable.");
                return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteUnavailable);
            }
            catch (TaskCanceledException)
            {
                Logs.Print("Sync remote problem delete failed: request timed out.");
                return SettingsDialogResult.Failed(UiStrings.SettingsSyncRemoteUnavailable);
            }
        }
    }

    private static string ToSyncSettingsErrorMessage(SyncApplicationServiceFactoryStatus status)
    {
        return status switch
        {
            SyncApplicationServiceFactoryStatus.MissingWebDavUrl => UiStrings.SettingsSyncWebDavUrlRequired,
            SyncApplicationServiceFactoryStatus.InvalidWebDavUrl => UiStrings.SettingsSyncWebDavUrlInvalid,
            SyncApplicationServiceFactoryStatus.MissingUsername => UiStrings.SettingsSyncUsernameRequired,
            SyncApplicationServiceFactoryStatus.MissingCredentials => UiStrings.SettingsSyncPasswordRequired,
            _ => UiStrings.SettingsSyncNowFailed
        };
    }

    private static bool IsRecoverableLocalSyncException(Exception exception)
    {
        var fullTypeName = exception.GetType().FullName;
        return exception is InvalidOperationException or System.IO.IOException or System.Text.Json.JsonException ||
            string.Equals(fullTypeName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal);
    }

    private static bool IsValidSyncWebDavUrl(string webDavUrl)
    {
        return Uri.TryCreate(webDavUrl, UriKind.Absolute, out var parsedUri) &&
            parsedUri.Scheme is "http" or "https";
    }

    private static string ToSyncSummaryErrorMessage(SyncRunSummary summary)
    {
        return summary.BlockingReason switch
        {
            SyncBlockingReason.LocalOperationActive => UiStrings.SettingsSyncLocalOperationActive,
            SyncBlockingReason.UnsupportedRepositoryVersion => UiStrings.SettingsSyncUnsupportedRepositoryVersion,
            SyncBlockingReason.InvalidRepository => UiStrings.SettingsSyncInvalidRepository,
            _ => UiStrings.SettingsSyncNowCompletedWithIssues
        };
    }

    private void OnMenuPopupClosed(object? sender, EventArgs e)
    {
        if (!StranichnikMenuPopup.IsOpen)
            SetMenuButtonOpen(StranichnikMenuButton, false);

        if (!ServiceMenuPopup.IsOpen)
            SetMenuButtonOpen(ServiceMenuButton, false);
    }

    private void ToggleMenuPopup(Popup popup, Button button)
    {
        var shouldOpen = !popup.IsOpen;

        CloseTreeContextMenu();
        CloseMenuPopups();

        popup.IsOpen = shouldOpen;
        SetMenuButtonOpen(button, shouldOpen);
    }

    private void OpenOnlyMenuPopup(Popup popup, Button button)
    {
        CloseTreeContextMenu();
        CloseMenuPopups();
        popup.IsOpen = true;
        SetMenuButtonOpen(button, true);
    }

    private void CloseMenuPopups()
    {
        StranichnikMenuPopup.IsOpen = false;
        ServiceMenuPopup.IsOpen = false;
        SetMenuButtonOpen(StranichnikMenuButton, false);
        SetMenuButtonOpen(ServiceMenuButton, false);
    }

    private void CloseAllPopups()
    {
        CloseMenuPopups();
        CloseTreeContextMenu();
    }

    private void OnMenuPopupShadowHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NotifySecretActivity();

        if (IsInsideMenuPopup(e.Source))
            return;

        CloseAllPopups();
        e.Handled = true;
    }

    private void CloseTreeContextMenu()
    {
        TreeContextMenuPopup.IsOpen = false;
        _contextMenuItem = null;
    }

    private static void SetMenuButtonOpen(Button button, bool isOpen)
    {
        if (isOpen)
        {
            if (!button.Classes.Contains("open"))
                button.Classes.Add("open");

            return;
        }

        button.Classes.Remove("open");
    }

    private static bool IsInsideMenuPopup(object? source)
    {
        if (source is not Visual visual)
            return false;

        if (IsMenuPopupBorder(visual))
            return true;

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (IsMenuPopupBorder(ancestor))
                return true;
        }

        return false;
    }

    private static bool IsMenuPopupBorder(Visual visual)
    {
        return visual is Border border && border.Classes.Contains("menuPopup");
    }

    private void ShowTreeContextMenu(BookmarkTreeItemViewModel item, Control placementTarget)
    {
        CloseMenuPopups();
        ClearTreePressState();

        _contextMenuItem = item;

        var isBookmark = item is BookmarkViewModel;
        var isFolder = item is BookmarkFolderViewModel;
        var isRootFolder = item is BookmarkFolderViewModel { IsRoot: true };

        ContextGoBookmarkMenuItem.IsVisible = isBookmark;
        ContextCopyBookmarkUrlMenuItem.IsVisible = isBookmark;
        ContextBookmarkSeparator.IsVisible = isBookmark;
        ContextAddBookmarkMenuItem.IsVisible = isFolder;
        ContextAddFolderMenuItem.IsVisible = isFolder;
        ContextEditMenuItem.IsVisible = !isRootFolder;
        ContextDeleteMenuItem.IsVisible = !isRootFolder;

        TreeContextMenuPopup.IsOpen = false;
        TreeContextMenuPopup.PlacementTarget = placementTarget;
        TreeContextMenuPopup.IsOpen = true;
    }

    private async void OnContextGoBookmarkClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkViewModel bookmark)
            await OpenBookmarkAsync(bookmark);

        e.Handled = true;
    }

    private async void OnContextCopyBookmarkUrlClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkViewModel bookmark)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(bookmark.Url);
        }

        e.Handled = true;
    }

    private async void OnContextAddBookmarkClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkFolderViewModel folder)
            await AddBookmarkToFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnContextAddFolderClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkFolderViewModel folder)
            await AddFolderToFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnContextEditClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkViewModel bookmark)
            await EditBookmarkAsync(bookmark);
        else if (item is BookmarkFolderViewModel folder && !folder.IsRoot)
            await EditFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnContextDeleteClick(object? sender, RoutedEventArgs e)
    {
        NotifySecretActivity();
        var item = _contextMenuItem;
        CloseTreeContextMenu();

        if (item is BookmarkViewModel bookmark)
            await DeleteBookmarkAsync(bookmark);
        else if (item is BookmarkFolderViewModel folder && !folder.IsRoot)
            await DeleteFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        NotifySecretActivity();

        if (e.Key == Key.Escape && HasOpenMenuPopup())
        {
            CloseAllPopups();
            e.Handled = true;
            return;
        }

        if (IsManualSyncShortcut(e))
        {
            e.Handled = true;
            await RunManualSyncFromShortcutAsync();
            return;
        }

        if (IsSecretToggleShortcut(e))
        {
            e.Handled = true;
            await ToggleSecretBookmarksAsync(closePopups: true);
        }
    }

    private static bool IsManualSyncShortcut(KeyEventArgs e)
    {
        if (e.Key != Key.S)
            return false;

        return HasPrimaryShortcutModifier(e) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private static bool IsSecretToggleShortcut(KeyEventArgs e)
    {
        if (e.Key != Key.P)
            return false;

        return HasPrimaryShortcutModifier(e) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private static bool HasPrimaryShortcutModifier(KeyEventArgs e)
    {
        var requiredModifier = OperatingSystem.IsMacOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

        return e.KeyModifiers.HasFlag(requiredModifier);
    }

    private bool HasOpenMenuPopup()
    {
        return StranichnikMenuPopup.IsOpen || ServiceMenuPopup.IsOpen || TreeContextMenuPopup.IsOpen;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = DataContext as MainWindowViewModel;

        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateBookmarksHorizontalOverflow();
        UpdateSecretInactivityTimer();
        UpdateSecretVisibilityMenuState();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _syncActivityService.ActivityChanged -= OnSyncActivityChanged;
        _syncActivityService.ActivityCompleted -= OnSyncActivityCompleted;
        _syncProgressAnimationTimer.Stop();
        _syncProgressCompletionGeneration++;
    }

    private void OnSyncActivityChanged(object? sender, SyncActivityChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => SetSyncProgressActive(e.IsActive));
    }

    private void OnSyncActivityCompleted(object? sender, SyncActivityCompletedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_syncActivityService.IsActive)
                _pendingSyncProgressCompletionSucceeded = e.Succeeded;
            else
                ShowSyncProgressCompletion(e.Succeeded);
        });
    }

    private void SetSyncProgressActive(bool isActive)
    {
        if (!isActive)
        {
            _syncProgressAnimationTimer.Stop();

            if (_isSyncProgressCompletionVisible)
                return;

            if (_pendingSyncProgressCompletionSucceeded is { } succeeded)
            {
                _pendingSyncProgressCompletionSucceeded = null;
                ShowSyncProgressCompletion(succeeded);
                return;
            }

            HideSyncProgressBar();
            return;
        }

        _syncProgressCompletionGeneration++;
        _pendingSyncProgressCompletionSucceeded = null;
        _isSyncProgressCompletionVisible = false;
        _syncProgressAnimationStartedAt = DateTimeOffset.UtcNow;
        SyncProgressBarHost.IsVisible = true;
        SyncProgressBarFill.Background = GetBrush(ThemeResourceKeys.FocusBorderBrush, Brushes.SteelBlue);
        SyncProgressBarFill.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _syncProgressAnimationTimer.Start();
        UpdateSyncProgressBar();
    }

    private void OnSyncProgressAnimationTimerTick(object? sender, EventArgs e)
    {
        UpdateSyncProgressBar();
    }

    private void UpdateSyncProgressBar()
    {
        var availableWidth = SyncProgressBarTrack.Bounds.Width;
        if (availableWidth <= 0)
            return;

        SyncProgressBarFill.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;

        var halfCycleMilliseconds = SyncProgressAnimationHalfCycle.TotalMilliseconds;
        var elapsedMilliseconds = (DateTimeOffset.UtcNow - _syncProgressAnimationStartedAt).TotalMilliseconds;
        var cyclePosition = elapsedMilliseconds % (halfCycleMilliseconds * 2);

        if (cyclePosition <= halfCycleMilliseconds)
        {
            var progress = cyclePosition / halfCycleMilliseconds;
            SyncProgressBarFill.Margin = new Avalonia.Thickness(0);
            SyncProgressBarFill.Width = Math.Max(1, availableWidth * progress);
            return;
        }

        var shrinkProgress = (cyclePosition - halfCycleMilliseconds) / halfCycleMilliseconds;
        SyncProgressBarFill.Margin = new Avalonia.Thickness(availableWidth * shrinkProgress, 0, 0, 0);
        SyncProgressBarFill.Width = Math.Max(1, availableWidth * (1 - shrinkProgress));
    }

    private void ShowSyncProgressCompletion(bool succeeded)
    {
        var generation = ++_syncProgressCompletionGeneration;
        _isSyncProgressCompletionVisible = true;
        SyncProgressBarHost.IsVisible = true;
        SyncProgressBarFill.Background = succeeded
            ? GetBrush(ThemeResourceKeys.SyncProgressSuccessBrush, Brushes.ForestGreen)
            : GetBrush(ThemeResourceKeys.SyncProgressErrorBrush, Brushes.Firebrick);
        SyncProgressBarFill.Margin = new Avalonia.Thickness(0);
        SyncProgressBarFill.Width = double.NaN;
        SyncProgressBarFill.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _ = HideSyncProgressCompletionLaterAsync(generation);
    }

    private async Task HideSyncProgressCompletionLaterAsync(int generation)
    {
        await Task.Delay(SyncProgressCompletionHoldDuration).ConfigureAwait(true);

        if (generation != _syncProgressCompletionGeneration ||
            _syncActivityService.IsActive)
        {
            return;
        }

        HideSyncProgressBar();
    }

    private void HideSyncProgressBar()
    {
        SyncProgressBarHost.IsVisible = false;
        _isSyncProgressCompletionVisible = false;
        SyncProgressBarFill.Background = GetBrush(ThemeResourceKeys.FocusBorderBrush, Brushes.SteelBlue);
        SyncProgressBarFill.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        SyncProgressBarFill.Width = 0;
        SyncProgressBarFill.Margin = new Avalonia.Thickness(0);
    }

    private IBrush GetBrush(string key, IBrush fallback)
    {
        if (TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
            return brush;

        return Application.Current?.TryGetResource(key, ActualThemeVariant, out resource) == true &&
            resource is IBrush appBrush
            ? appBrush
            : fallback;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.AreSecretsVisible))
        {
            UpdateSecretInactivityTimer();
            UpdateSecretVisibilityMenuState();
        }
    }

    private void OnWindowActivityPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NotifySecretActivity();
    }

    private void OnWindowActivityPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        NotifySecretActivity();
    }

    private void OnWindowActivityPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        NotifySecretActivity();
    }

    private void NotifySecretActivity()
    {
        _secretInactivityController.NotifyActivity();
    }

    private void UpdateSecretInactivityTimer()
    {
        _secretInactivityController.SetSecretsVisible(_observedViewModel?.AreSecretsVisible == true);
    }

    private void UpdateSecretVisibilityMenuState()
    {
        var areSecretsVisible = _observedViewModel?.AreSecretsVisible == true;

        SecretVisibilitySwitchText.Text = areSecretsVisible
            ? UiStrings.CommonOn
            : UiStrings.CommonOff;

        if (areSecretsVisible)
        {
            if (!SecretVisibilitySwitch.Classes.Contains("on"))
                SecretVisibilitySwitch.Classes.Add("on");

            return;
        }

        SecretVisibilitySwitch.Classes.Remove("on");
    }

    private void OnSecretInactivityTimeout()
    {
        if (_observedViewModel?.AreSecretsVisible != true)
            return;

        _observedViewModel.HideSecretsByInactivityTimeout();
        UpdateBookmarksHorizontalOverflow();
    }

    private void OnTreeRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (sender is Control control &&
                control.DataContext is BookmarkTreeItemViewModel contextItem)
            {
                ShowTreeContextMenu(contextItem, control);
                e.Handled = true;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control source && HasButtonAncestor(source))
            return;

        if (sender is not Control { DataContext: BookmarkTreeItemViewModel item })
            return;

        _pressedTreeItem = item;
        _pressedFolderClickCandidate = item is BookmarkFolderViewModel ? item : null;
        _treeDragStartPoint = e.GetCurrentPoint(BookmarksScrollViewer).Position;
        _lastTreeDragPoint = _treeDragStartPoint;

        e.Pointer.Capture(BookmarksScrollViewer);
        e.Handled = true;
    }

    private async void OnAddBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkFolderViewModel folder })
            await AddBookmarkToFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnOpenBookmarkClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: BookmarkViewModel bookmark })
            await OpenBookmarkAsync(bookmark);
    }

    private async void OnOpenSearchResultClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: BookmarkSearchResultItem result })
            return;

        if (!TryOpenBookmarkUrl(result.Url, out var errorMessage))
            await MessageDialog.ShowError(this, UiStrings.ErrorOpenPageTitle, errorMessage);
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ClearSearch();

        e.Handled = true;
    }

    private async void OnEditBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkViewModel bookmark })
            await EditBookmarkAsync(bookmark);

        e.Handled = true;
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkFolderViewModel folder })
            await AddFolderToFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnEditFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkFolderViewModel folder })
            await EditFolderAsync(folder);

        e.Handled = true;
    }

    private async void OnDeleteBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkViewModel bookmark })
            await DeleteBookmarkAsync(bookmark);

        e.Handled = true;
    }

    private async void OnDeleteFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BookmarkFolderViewModel folder })
            await DeleteFolderAsync(folder);

        e.Handled = true;
    }

    private async Task AddBookmarkToFolderAsync(BookmarkFolderViewModel folder)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var dialog = BookmarkEditorDialog.AddBookmark(owner =>
            EnsureSecretEditingAvailableAsync(viewModel, owner),
            (owner, request) => ChooseIconFromLibraryAsync(owner, viewModel, request));
        var result = await ShowBookmarkEditorDialogAsync(dialog);

        if (result is null)
            return;

        folder = viewModel.FindFolder(folder.Id) ?? folder;

        var addResult = result.IsSecret
            ? AddSecretBookmarkWithSyncGate(viewModel, folder, result)
            : AddBookmarkWithSyncGate(viewModel, folder, result);

        if (addResult.WasAdded)
        {
            folder.IsExpanded = true;
            UpdateBookmarksHorizontalOverflow();
        }
    }

    private BookmarkTreeAddBookmarkResult AddBookmarkWithSyncGate(
        MainWindowViewModel viewModel,
        BookmarkFolderViewModel folder,
        BookmarkEditorDialogResult result)
    {
        using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
        return viewModel.AddBookmarkToFolderStart(
            folder,
            result.Title,
            result.Url,
            result.IconSelection);
    }

    private BookmarkTreeAddBookmarkResult AddSecretBookmarkWithSyncGate(
        MainWindowViewModel viewModel,
        BookmarkFolderViewModel folder,
        BookmarkEditorDialogResult result)
    {
        using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
        return viewModel.AddSecretBookmarkToFolderStart(
            folder,
            result.Title,
            result.Url,
            result.IconSelection);
    }

    private async Task AddFolderToFolderAsync(BookmarkFolderViewModel folder)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var dialog = BookmarkEditorDialog.AddFolder((owner, request) =>
            ChooseIconFromLibraryAsync(owner, viewModel, request));
        var result = await ShowBookmarkEditorDialogAsync(dialog);

        if (result is null)
            return;

        folder = viewModel.FindFolder(folder.Id) ?? folder;

        var addResult = AddFolderWithSyncGate(viewModel, folder, result);

        if (addResult.WasAdded)
        {
            folder.IsExpanded = true;
            UpdateBookmarksHorizontalOverflow();
        }
    }

    private BookmarkTreeAddFolderResult AddFolderWithSyncGate(
        MainWindowViewModel viewModel,
        BookmarkFolderViewModel folder,
        BookmarkEditorDialogResult result)
    {
        using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
        return viewModel.AddFolderToFolderStart(folder, result.Title, result.IconSelection);
    }

    private async Task OpenBookmarkAsync(BookmarkViewModel bookmark)
    {
        if (!TryOpenBookmarkUrl(bookmark.Url, out var errorMessage))
            await MessageDialog.ShowError(this, UiStrings.ErrorOpenPageTitle, errorMessage);
    }

    private async Task EditBookmarkAsync(BookmarkViewModel bookmark)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var dialog = BookmarkEditorDialog.EditBookmark(
            bookmark.Title,
            bookmark.Url,
            bookmark.IconImage,
            bookmark.IsSecret,
            owner => EnsureSecretEditingAvailableAsync(viewModel, owner),
            (owner, request) => ChooseIconFromLibraryAsync(owner, viewModel, request));
        var result = await ShowBookmarkEditorDialogAsync(dialog);

        if (result is not null)
        {
            bookmark = viewModel.FindBookmark(bookmark.Id) ?? bookmark;

            if (result.IsSecret)
            {
                using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
                viewModel.EditBookmarkAsSecret(bookmark, result.Title, result.Url, result.IconSelection);
            }
            else if (bookmark.IsSecret)
            {
                using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
                viewModel.EditSecretBookmarkAsPlaintext(
                    bookmark,
                    result.Title,
                    result.Url,
                    result.IconSelection);
            }
            else
            {
                using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
                viewModel.EditBookmark(bookmark, result.Title, result.Url, result.IconSelection);
            }

            UpdateBookmarksHorizontalOverflow();
        }
    }

    private async Task<bool> EnsureSecretEditingAvailableAsync(
        MainWindowViewModel viewModel,
        Window owner)
    {
        if (viewModel.IsSecretSessionUnlocked)
            return true;

        if (viewModel.IsSecretProfileConfigured)
            return await ShowUnlockSecretsDialogAsync(owner, viewModel, showSecrets: true);

        var dialog = new SetMasterPasswordDialog();
        var result = await dialog.ShowDialog<SetMasterPasswordDialogResult?>(owner);

        if (result is null)
            return false;

        SecretProfileSetupResult setupResult;
        using (_syncOperationGate.EnterLocalWriteOperation())
            setupResult = viewModel.CreateMasterPassword(result.MasterPassword, showSecrets: true);

        if (setupResult.WasCreated)
            return true;

        await MessageDialog.ShowMessage(
            owner,
            UiStrings.SecretSetupFailedTitle,
            UiStrings.SecretSetupFailedMessage);
        return false;
    }

    private async Task ToggleSecretBookmarksAsync(bool closePopups)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (closePopups)
            CloseAllPopups();

        if (!viewModel.IsSecretProfileConfigured)
        {
            await MessageDialog.ShowMessage(
                this,
                UiStrings.UnlockSecretsNotConfiguredTitle,
                UiStrings.UnlockSecretsNotConfiguredMessage);
            return;
        }

        if (viewModel.AreSecretsVisible)
        {
            viewModel.HideSecretsByUserAction();
            UpdateBookmarksHorizontalOverflow();
            return;
        }

        if (viewModel.IsSecretSessionUnlocked)
        {
            viewModel.ShowSecrets();
            UpdateBookmarksHorizontalOverflow();
            return;
        }

        if (await ShowUnlockSecretsDialogAsync(viewModel, showSecrets: true))
            UpdateBookmarksHorizontalOverflow();
    }

    private async Task<bool> ShowUnlockSecretsDialogAsync(
        MainWindowViewModel viewModel,
        bool showSecrets)
    {
        return await ShowUnlockSecretsDialogAsync(this, viewModel, showSecrets);
    }

    private static async Task<bool> ShowUnlockSecretsDialogAsync(
        Window owner,
        MainWindowViewModel viewModel,
        bool showSecrets)
    {
        var dialog = new UnlockSecretsDialog(password =>
            viewModel.UnlockSecrets(password, showSecrets).WasUnlocked);
        var result = await dialog.ShowDialog<UnlockSecretsDialogResult?>(owner);

        return result?.WasUnlocked == true;
    }

    private async Task EditFolderAsync(BookmarkFolderViewModel folder)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var dialog = BookmarkEditorDialog.EditFolder(
            folder.Title,
            folder.IconImage,
            (owner, request) => ChooseIconFromLibraryAsync(owner, viewModel, request));
        var result = await ShowBookmarkEditorDialogAsync(dialog);

        if (result is not null)
        {
            using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
            viewModel.EditFolder(folder, result.Title, result.IconSelection);
            UpdateBookmarksHorizontalOverflow();
        }
    }

    private async Task DeleteBookmarkAsync(BookmarkViewModel bookmark)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var confirmed = await ConfirmDialog.ShowDeleteBookmark(this, bookmark.Title);
        if (confirmed)
        {
            using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
            viewModel.DeleteItem(bookmark);
            UpdateBookmarksHorizontalOverflow();
        }
    }

    private static Task<IconLibraryDialogResult?> ChooseIconFromLibraryAsync(
        Window owner,
        MainWindowViewModel viewModel,
        IconLibraryRequest request)
    {
        var items = viewModel.BuildIconLibrary(request);
        return IconLibraryDialog.ShowAsync(owner, items);
    }

    private async Task<BookmarkEditorDialogResult?> ShowBookmarkEditorDialogAsync(BookmarkEditorDialog dialog)
    {
        _secretInactivityController.Pause();
        using var editorSession = _syncOperationGate.EnterEditorSession();
        try
        {
            return await dialog.ShowDialog<BookmarkEditorDialogResult?>(this);
        }
        finally
        {
            _secretInactivityController.Resume();
        }
    }

    private sealed class DispatcherSecretInactivityTimer : ISecretInactivityTimer
    {
        private readonly DispatcherTimer _timer;

        public DispatcherSecretInactivityTimer(DispatcherTimer timer)
        {
            _timer = timer;
        }

        public event EventHandler? Tick
        {
            add => _timer.Tick += value;
            remove => _timer.Tick -= value;
        }

        public void StartTimer()
        {
            _timer.Start();
        }

        public void StopTimer()
        {
            _timer.Stop();
        }
    }

    private async Task DeleteFolderAsync(BookmarkFolderViewModel folder)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var confirmed = await ConfirmDialog.ShowDeleteFolder(this, folder.Title);
        if (confirmed)
        {
            using var writeOperation = _syncOperationGate.EnterLocalWriteOperation();
            viewModel.DeleteItem(folder);
            UpdateBookmarksHorizontalOverflow();
        }
    }

    private static bool TryOpenBookmarkUrl(string url, out string errorMessage)
    {
        var normalizeStatus = BookmarkUrlNormalizer.TryNormalizeForOpening(url, out var uri);
        if (normalizeStatus == BookmarkUrlOpenStatus.InvalidAddress)
        {
            errorMessage = UiStrings.ErrorInvalidBookmarkUrl;
            return false;
        }

        if (normalizeStatus == BookmarkUrlOpenStatus.UnsupportedScheme)
        {
            errorMessage = UiStrings.ErrorUnsupportedBookmarkUrlScheme;
            return false;
        }

        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Win32Exception)
        {
            errorMessage = UiStrings.ErrorCannotLaunchBrowser;
            return false;
        }
        catch (InvalidOperationException)
        {
            errorMessage = UiStrings.ErrorUrlBrowserStartFailed;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private void OnFolderRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isTreeDragging)
        {
            CompleteTreeDrag(e);
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (e.Source is Control source && HasButtonAncestor(source))
            return;

        if (sender is not Control { DataContext: BookmarkFolderViewModel folder })
            return;

        if (_pressedTreeItem != folder)
        {
            _pressedTreeItem = null;
            return;
        }

        ToggleFolderExpansionPreservingPosition(folder);
        ClearTreePressState(e);
        e.Handled = true;
    }

    private void OnTreeRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isTreeDragging)
        {
            CompleteTreeDrag(e);
            return;
        }

        ClearTreePressState(e);
    }

    private void OnBookmarksScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(BookmarksScrollViewer);
        if (!point.Properties.IsMiddleButtonPressed)
            return;

        _isMiddleButtonPanning = true;
        _panStartPoint = point.Position;
        _panStartOffset = BookmarksScrollViewer.Offset;

        e.Pointer.Capture(BookmarksScrollViewer);
        e.Handled = true;
    }

    private void OnBookmarksScrollPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMiddleButtonPanning)
        {
            UpdateTreeDrag(e);
            return;
        }

        var currentPoint = e.GetCurrentPoint(BookmarksScrollViewer).Position;
        var delta = currentPoint - _panStartPoint;
        var requestedOffset = _panStartOffset - delta;

        BookmarksScrollViewer.Offset = new Vector(
            ClampOffset(requestedOffset.X, BookmarksScrollViewer.Extent.Width, BookmarksScrollViewer.Viewport.Width),
            ClampOffset(requestedOffset.Y, BookmarksScrollViewer.Extent.Height, BookmarksScrollViewer.Viewport.Height));

        e.Handled = true;
    }

    private void OnBookmarksScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMiddleButtonPanning || e.InitialPressMouseButton != MouseButton.Middle)
        {
            if (_isTreeDragging)
                CompleteTreeDrag(e);
            else
                CompletePendingFolderClick(e);

            return;
        }

        StopMiddleButtonPanning(e);
    }

    private void OnBookmarksScrollPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isMiddleButtonPanning = false;
        ClearTreeDrag();
    }

    private void UpdateBookmarksHorizontalOverflow()
    {
        var maxVisibleDepth = DataContext is MainWindowViewModel viewModel
            ? GetMaxVisibleDepth(new[] { viewModel.RootFolder }, depth: 0)
            : 0;

        var estimatedContentWidth = maxVisibleDepth * TreeIndentWidth + OverflowRowWidth;
        var hasHorizontalOverflow = estimatedContentWidth > BookmarksScrollViewer.Viewport.Width;

        BookmarksScrollViewer.Classes.Set("hasHorizontalOverflow", hasHorizontalOverflow);
    }

    private void UpdateTreeDrag(PointerEventArgs e)
    {
        if (_pressedTreeItem is null)
            return;

        if (_pressedTreeItem is BookmarkFolderViewModel { IsRoot: true })
            return;

        var point = e.GetCurrentPoint(BookmarksScrollViewer);
        _lastTreeDragPoint = point.Position;

        if (!point.Properties.IsLeftButtonPressed)
        {
            ClearTreeDrag();
            return;
        }

        var delta = point.Position - _treeDragStartPoint;
        if (!_isTreeDragging &&
            Math.Abs(delta.X) < DragStartThreshold &&
            Math.Abs(delta.Y) < DragStartThreshold)
        {
            return;
        }

        if (!_isTreeDragging)
            StartTreeDrag(_pressedTreeItem, e);

        UpdateDragGhostPosition(e);
        UpdateDropTarget(e);
        e.Handled = true;
    }

    private void StartTreeDrag(BookmarkTreeItemViewModel item, PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var hasInitialWindowY = TryGetTreeRowWindowY(item, out var initialWindowY);
        _hasDragStartWindowY = hasInitialWindowY;
        _dragStartWindowY = initialWindowY;

        _isTreeDragging = true;
        _draggedTreeItem = item;

        if (item is BookmarkFolderViewModel { IsExpanded: true } draggedFolder)
        {
            draggedFolder.IsExpanded = false;
            UpdateBookmarksHorizontalOverflow();
        }

        _dragAutoScrollTimer.Start();
        viewModel.ShowDropPlaceholders(item);
        ShowDragGhost(item, e);

        if (hasInitialWindowY)
            PreserveDraggedRowWindowYAfterLayout(item, initialWindowY);
    }

    private void UpdateDropTarget(PointerEventArgs e)
    {
        if (_draggedTreeItem is null || DataContext is not MainWindowViewModel viewModel)
            return;

        var source = FindControlAt(e);
        UpdateDragHoverTarget(source);
        ScheduleFolderAutoExpand(source);

        if (TryGetDropTarget(source, out var targetFolder) &&
            viewModel.CanMoveItemToFolder(_draggedTreeItem, targetFolder))
        {
            _hasActiveDropTarget = true;
            _activeDropTargetFolder = targetFolder;
            viewModel.ActivateDropPlaceholder(targetFolder);
            return;
        }

        _hasActiveDropTarget = false;
        _activeDropTargetFolder = null;
        viewModel.ClearActiveDropPlaceholder();
    }

    private void UpdateDragHoverTarget(Control? source)
    {
        if (_draggedTreeItem is null || DataContext is not MainWindowViewModel viewModel)
            return;

        var folder = FindTreeRow(source)?.DataContext as BookmarkFolderViewModel;
        if (folder is { IsExpanded: false } &&
            viewModel.CanMoveItemToFolder(_draggedTreeItem, folder))
        {
            viewModel.SetDragHoverFolder(folder);
            return;
        }

        viewModel.SetDragHoverFolder(targetFolder: null);
    }

    private void CompleteTreeDrag(PointerEventArgs e)
    {
        if (_draggedTreeItem is null || DataContext is not MainWindowViewModel viewModel)
        {
            ClearTreeDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        var draggedItem = _draggedTreeItem;
        var wasMoved = false;
        var anchorWindowY = 0d;
        var hasAnchorWindowY = _hasActiveDropTarget
            ? TryGetDropPlaceholderWindowY(_activeDropTargetFolder, out anchorWindowY)
            : TryGetDragStartWindowY(out anchorWindowY);

        if (_hasActiveDropTarget)
        {
            BookmarkTreeMoveResult moveResult;
            using (_syncOperationGate.EnterLocalWriteOperation())
                moveResult = viewModel.MoveItemToFolderStart(draggedItem, _activeDropTargetFolder);

            wasMoved = moveResult.WasMoved;
            UpdateBookmarksHorizontalOverflow();

            if (!wasMoved)
                hasAnchorWindowY = TryGetDragStartWindowY(out anchorWindowY);
        }

        ClearTreeDrag();

        if (hasAnchorWindowY)
            PreserveTreeRowWindowYAfterLayout(draggedItem, anchorWindowY);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CompletePendingFolderClick(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            ClearTreePressState(e);
            return;
        }

        var row = FindTreeRow(FindControlAt(e));
        if (_pressedFolderClickCandidate is BookmarkFolderViewModel folder &&
            row?.DataContext == folder &&
            (e.Source is not Control source || !HasButtonAncestor(source)))
        {
            ToggleFolderExpansionPreservingPosition(folder);
            e.Handled = true;
        }

        ClearTreePressState(e);
    }

    private void ClearTreePressState(PointerEventArgs e)
    {
        ClearTreePressState();
        e.Pointer.Capture(null);
    }

    private void ClearTreePressState()
    {
        _pressedTreeItem = null;
        _pressedFolderClickCandidate = null;
    }

    private void ClearTreeDrag()
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ClearDropPlaceholders();

        _folderAutoExpandTimer.Stop();
        _dragAutoScrollTimer.Stop();
        _pressedTreeItem = null;
        _pressedFolderClickCandidate = null;
        _draggedTreeItem = null;
        _activeDropTargetFolder = null;
        _pendingAutoExpandFolder = null;
        _isTreeDragging = false;
        _hasActiveDropTarget = false;
        _hasDragStartWindowY = false;
        _dragStartWindowY = 0;
        HideDragGhost();
    }

    private void ScheduleFolderAutoExpand(Control? source)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            _draggedTreeItem is null ||
            FindTreeRow(source)?.DataContext is not BookmarkFolderViewModel { IsExpanded: false } folder ||
            !viewModel.CanMoveItemToFolder(_draggedTreeItem, folder))
        {
            _folderAutoExpandTimer.Stop();
            _pendingAutoExpandFolder = null;
            return;
        }

        if (_pendingAutoExpandFolder == folder)
            return;

        _pendingAutoExpandFolder = folder;
        _folderAutoExpandTimer.Stop();
        _folderAutoExpandTimer.Start();
    }

    private void OnFolderAutoExpandTimerTick(object? sender, EventArgs e)
    {
        _folderAutoExpandTimer.Stop();

        if (!_isTreeDragging ||
            _draggedTreeItem is null ||
            _pendingAutoExpandFolder is null ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var expandingFolder = _pendingAutoExpandFolder;
        var hasInitialWindowY = TryGetTreeRowWindowY(expandingFolder, out var initialWindowY);

        expandingFolder.IsExpanded = true;
        _pendingAutoExpandFolder = null;
        viewModel.ShowDropPlaceholders(_draggedTreeItem);
        UpdateBookmarksHorizontalOverflow();

        if (hasInitialWindowY)
            Dispatcher.UIThread.Post(() => PreserveTreeRowWindowY(expandingFolder, initialWindowY));
    }

    private void OnDragAutoScrollTimerTick(object? sender, EventArgs e)
    {
        if (!_isTreeDragging)
        {
            _dragAutoScrollTimer.Stop();
            return;
        }

        var viewport = BookmarksScrollViewer.Viewport;
        if (viewport.Width <= 0 && viewport.Height <= 0)
            return;

        var horizontalStep = 0d;
        var verticalStep = 0d;

        if (viewport.Width > 0)
        {
            var distanceFromLeft = _lastTreeDragPoint.X;
            var distanceFromRight = viewport.Width - _lastTreeDragPoint.X;

            if (distanceFromLeft < DragAutoScrollEdgeSize)
                horizontalStep = -GetAutoScrollStep(distanceFromLeft);
            else if (distanceFromRight < DragAutoScrollEdgeSize)
                horizontalStep = GetAutoScrollStep(distanceFromRight);
        }

        if (viewport.Height > 0)
        {
            var distanceFromTop = _lastTreeDragPoint.Y;
            var distanceFromBottom = viewport.Height - _lastTreeDragPoint.Y;

            if (distanceFromTop < DragAutoScrollEdgeSize)
                verticalStep = -GetAutoScrollStep(distanceFromTop);
            else if (distanceFromBottom < DragAutoScrollEdgeSize)
                verticalStep = GetAutoScrollStep(distanceFromBottom);
        }

        if (horizontalStep == 0 && verticalStep == 0)
            return;

        var currentOffset = BookmarksScrollViewer.Offset;
        BookmarksScrollViewer.Offset = new Vector(
            ClampOffset(
                currentOffset.X + horizontalStep,
                BookmarksScrollViewer.Extent.Width,
                viewport.Width),
            ClampOffset(
                currentOffset.Y + verticalStep,
                BookmarksScrollViewer.Extent.Height,
                viewport.Height));
    }

    private static double GetAutoScrollStep(double distanceFromEdge)
    {
        var intensity = 1 - Math.Clamp(distanceFromEdge, 0, DragAutoScrollEdgeSize) / DragAutoScrollEdgeSize;
        return Math.Max(1, intensity * DragAutoScrollMaxStep);
    }

    private void ShowDragGhost(BookmarkTreeItemViewModel item, PointerEventArgs e)
    {
        DragGhostTitle.Text = item.Title;

        if (item is BookmarkViewModel bookmark)
        {
            DragGhostUrl.Text = bookmark.Url;
            DragGhostUrl.IsVisible = true;
        }
        else
        {
            DragGhostUrl.Text = string.Empty;
            DragGhostUrl.IsVisible = false;
        }

        DragGhost.IsVisible = true;
        DragGhost.Opacity = 0;
        _dragGhostScaleTransform.ScaleX = DragGhostInitialScale;
        _dragGhostScaleTransform.ScaleY = DragGhostInitialScale;
        _dragGhostAnimationStartedAt = DateTimeOffset.UtcNow;
        _dragGhostAnimationTimer.Stop();
        _dragGhostAnimationTimer.Start();
        UpdateDragGhostPosition(e);
    }

    private void HideDragGhost()
    {
        _dragGhostAnimationTimer.Stop();
        DragGhost.IsVisible = false;
    }

    private void UpdateDragGhostPosition(PointerEventArgs e)
    {
        if (!DragGhost.IsVisible)
            return;

        var pointerPosition = e.GetCurrentPoint(DragGhostLayer).Position;
        var maxLeft = Math.Max(0, DragGhostLayer.Bounds.Width - DragGhost.Bounds.Width);
        var maxTop = Math.Max(0, DragGhostLayer.Bounds.Height - DragGhost.Bounds.Height);
        var left = Math.Clamp(pointerPosition.X + DragGhostOffsetX, 0, maxLeft);
        var top = Math.Clamp(pointerPosition.Y - DragGhost.Bounds.Height / 2, 0, maxTop);

        Canvas.SetLeft(DragGhost, left);
        Canvas.SetTop(DragGhost, top);
    }

    private void OnDragGhostAnimationTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTimeOffset.UtcNow - _dragGhostAnimationStartedAt;
        var progress = Math.Clamp(
            elapsed.TotalMilliseconds / DragGhostAnimationDuration.TotalMilliseconds,
            0,
            1);

        var easedProgress = 1 - Math.Pow(1 - progress, 2);
        var scale = DragGhostInitialScale + (1 - DragGhostInitialScale) * easedProgress;

        DragGhost.Opacity = DragGhostOpacity * easedProgress;
        _dragGhostScaleTransform.ScaleX = scale;
        _dragGhostScaleTransform.ScaleY = scale;

        if (progress >= 1)
            _dragGhostAnimationTimer.Stop();
    }

    private static double ClampOffset(double offset, double extent, double viewport)
    {
        var maxOffset = Math.Max(0, extent - viewport);
        return Math.Clamp(offset, 0, maxOffset);
    }

    private void StopMiddleButtonPanning(PointerEventArgs e)
    {
        _isMiddleButtonPanning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private static int GetMaxVisibleDepth(IEnumerable<BookmarkTreeItemViewModel> items, int depth)
    {
        var maxDepth = depth;

        foreach (var item in items)
        {
            maxDepth = Math.Max(maxDepth, depth);

            if (item is BookmarkFolderViewModel { IsExpanded: true } folder)
            {
                maxDepth = Math.Max(maxDepth, GetMaxVisibleDepth(folder.Children, depth + 1));
            }
        }

        return maxDepth;
    }

    private Control? FindControlAt(PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(BookmarksScrollViewer).Position;
        return BookmarksScrollViewer.InputHitTest(point) as Control;
    }

    private bool TryGetTreeRowWindowY(BookmarkTreeItemViewModel? item, out double windowY)
    {
        windowY = 0;

        if (item is null)
            return false;

        foreach (var visual in BookmarksScrollViewer.GetVisualDescendants())
        {
            if (visual is not Border border ||
                !border.Classes.Contains("treeRow") ||
                !ReferenceEquals(border.DataContext, item))
            {
                continue;
            }

            var point = border.TranslatePoint(new Point(0, 0), this);
            if (point is null)
                return false;

            windowY = point.Value.Y;
            return true;
        }

        return false;
    }

    private bool TryGetDropPlaceholderWindowY(BookmarkFolderViewModel? targetFolder, out double windowY)
    {
        windowY = 0;

        foreach (var visual in BookmarksScrollViewer.GetVisualDescendants())
        {
            if (visual is not Border border ||
                !border.Classes.Contains("dropPlaceholder") ||
                !border.Classes.Contains("active"))
            {
                continue;
            }

            if (!ReferenceEquals(border.DataContext, targetFolder))
                continue;

            var point = border.TranslatePoint(new Point(0, 0), this);
            if (point is null)
                return false;

            windowY = point.Value.Y;
            return true;
        }

        return false;
    }

    private bool TryGetDragStartWindowY(out double windowY)
    {
        windowY = _dragStartWindowY;
        return _hasDragStartWindowY;
    }

    private void ToggleFolderExpansionPreservingPosition(BookmarkFolderViewModel folder)
    {
        var hasInitialWindowY = TryGetTreeRowWindowY(folder, out var initialWindowY);

        folder.IsExpanded = !folder.IsExpanded;
        UpdateBookmarksHorizontalOverflow();

        if (hasInitialWindowY)
            Dispatcher.UIThread.Post(() => PreserveTreeRowWindowY(folder, initialWindowY));
    }

    private void PreserveTreeRowWindowY(BookmarkTreeItemViewModel item, double initialWindowY)
    {
        if (!TryGetTreeRowWindowY(item, out var currentWindowY))
            return;

        PreserveWindowY(initialWindowY, currentWindowY);
    }

    private void PreserveTreeRowWindowYAfterLayout(BookmarkTreeItemViewModel item, double initialWindowY)
    {
        BookmarksHost.UpdateLayout();
        PreserveTreeRowWindowY(item, initialWindowY);
    }

    private void PreserveDraggedRowWindowY(BookmarkTreeItemViewModel item, double initialWindowY)
    {
        if (!_isTreeDragging ||
            !ReferenceEquals(_draggedTreeItem, item) ||
            !TryGetTreeRowWindowY(item, out var currentWindowY))
        {
            return;
        }

        PreserveWindowY(initialWindowY, currentWindowY);
    }

    private void PreserveDraggedRowWindowYAfterLayout(BookmarkTreeItemViewModel item, double initialWindowY)
    {
        BookmarksHost.UpdateLayout();
        PreserveDraggedRowWindowY(item, initialWindowY);
    }

    private void PreserveWindowY(double initialWindowY, double currentWindowY)
    {
        var delta = currentWindowY - initialWindowY;
        if (Math.Abs(delta) < 0.5)
            return;

        var currentOffset = BookmarksScrollViewer.Offset;
        BookmarksScrollViewer.Offset = new Vector(
            currentOffset.X,
            ClampOffset(
                currentOffset.Y + delta,
                BookmarksScrollViewer.Extent.Height,
                BookmarksScrollViewer.Viewport.Height));
    }

    private static Border? FindTreeRow(Control? control)
    {
        while (control is not null)
        {
            if (control is Border border && border.Classes.Contains("treeRow"))
                return border;

            control = control.GetVisualParent() as Control;
        }

        return null;
    }

    private static bool TryGetDropTarget(Control? control, out BookmarkFolderViewModel? targetFolder)
    {
        targetFolder = null;

        while (control is not null)
        {
            if (control is Border border && border.Classes.Contains("dropPlaceholder"))
            {
                if (border.DataContext is BookmarkFolderViewModel folder)
                {
                    targetFolder = folder;
                    return true;
                }
            }

            control = control.GetVisualParent() as Control;
        }

        return false;
    }

    private static bool HasButtonAncestor(Control? control)
    {
        while (control is not null)
        {
            if (control is Button)
                return true;

            control = control.GetVisualParent() as Control;
        }

        return false;
    }
}
