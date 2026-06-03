using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stranichnik.Localization;
using Stranichnik.Searching;
using Stranichnik.Settings;
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

    private bool _isMiddleButtonPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
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
    private readonly ScaleTransform _dragGhostScaleTransform = new()
    {
        ScaleX = 1,
        ScaleY = 1
    };
    private DateTimeOffset _dragGhostAnimationStartedAt;

    public MainWindow()
    {
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

        DataContextChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        BookmarksScrollViewer.SizeChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        UpdateBookmarksHorizontalOverflow();
    }

    private void OnStranichnikMenuClick(object? sender, RoutedEventArgs e)
    {
        ToggleMenuPopup(StranichnikMenuPopup, StranichnikMenuButton);
        e.Handled = true;
    }

    private void OnServiceMenuClick(object? sender, RoutedEventArgs e)
    {
        ToggleMenuPopup(ServiceMenuPopup, ServiceMenuButton);
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
            OpenOnlyMenuPopup(ServiceMenuPopup, ServiceMenuButton);
    }

    private void OnMenuPopupItemClick(object? sender, RoutedEventArgs e)
    {
        CloseMenuPopups();
        e.Handled = true;
    }

    private async void OnLanguageMenuClick(object? sender, RoutedEventArgs e)
    {
        CloseMenuPopups();
        e.Handled = true;

        var settings = AppSettingsService.Load();
        var currentLanguage = string.IsNullOrWhiteSpace(settings.Language)
            ? LanguageService.CurrentLanguage
            : settings.Language;
        var selectedLanguage = await LanguageDialog.Show(this, currentLanguage);
        if (selectedLanguage is null)
            return;

        settings.Language = selectedLanguage;
        AppSettingsService.Save(settings);
        LanguageService.Apply(selectedLanguage);

        await MessageDialog.ShowMessage(
            this,
            UiStrings.LanguageDialogRestartTitle,
            UiStrings.LanguageDialogRestartMessage);
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

        CloseMenuPopups();

        popup.IsOpen = shouldOpen;
        SetMenuButtonOpen(button, shouldOpen);
    }

    private void OpenOnlyMenuPopup(Popup popup, Button button)
    {
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !HasOpenMenuPopup())
            return;

        CloseMenuPopups();
        e.Handled = true;
    }

    private bool HasOpenMenuPopup()
    {
        return StranichnikMenuPopup.IsOpen || ServiceMenuPopup.IsOpen;
    }

    private void OnTreeRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkFolderViewModel folder })
        {
            return;
        }

        var dialog = BookmarkEditorDialog.AddBookmark();
        var result = await dialog.ShowDialog<BookmarkEditorDialogResult?>(this);

        if (result is null)
            return;

        var addResult = viewModel.AddBookmarkToFolderStart(folder, result.Title, result.Url);
        if (addResult.WasAdded)
        {
            folder.IsExpanded = true;
            UpdateBookmarksHorizontalOverflow();
        }

        e.Handled = true;
    }

    private async void OnOpenBookmarkClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: BookmarkViewModel bookmark })
            return;

        if (!TryOpenBookmarkUrl(bookmark.Url, out var errorMessage))
            await MessageDialog.ShowError(this, UiStrings.ErrorOpenPageTitle, errorMessage);
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
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkViewModel bookmark })
        {
            return;
        }

        var dialog = BookmarkEditorDialog.EditBookmark(bookmark.Title, bookmark.Url);
        var result = await dialog.ShowDialog<BookmarkEditorDialogResult?>(this);

        if (result is not null)
        {
            viewModel.EditBookmark(bookmark, result.Title, result.Url);
            UpdateBookmarksHorizontalOverflow();
        }

        e.Handled = true;
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkFolderViewModel folder })
        {
            return;
        }

        var dialog = BookmarkEditorDialog.AddFolder();
        var result = await dialog.ShowDialog<BookmarkEditorDialogResult?>(this);

        if (result is not null)
        {
            var addResult = viewModel.AddFolderToFolderStart(folder, result.Title);
            if (addResult.WasAdded)
            {
                folder.IsExpanded = true;
                UpdateBookmarksHorizontalOverflow();
            }
        }

        e.Handled = true;
    }

    private async void OnEditFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkFolderViewModel folder })
        {
            return;
        }

        var dialog = BookmarkEditorDialog.EditFolder(folder.Title);
        var result = await dialog.ShowDialog<BookmarkEditorDialogResult?>(this);

        if (result is not null)
        {
            viewModel.EditFolder(folder, result.Title);
            UpdateBookmarksHorizontalOverflow();
        }

        e.Handled = true;
    }

    private async void OnDeleteBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkViewModel bookmark })
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowDeleteBookmark(this, bookmark.Title);
        if (confirmed)
        {
            viewModel.DeleteItem(bookmark);
            UpdateBookmarksHorizontalOverflow();
        }

        e.Handled = true;
    }

    private async void OnDeleteFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: BookmarkFolderViewModel folder })
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowDeleteFolder(this, folder.Title);
        if (confirmed)
        {
            viewModel.DeleteItem(folder);
            UpdateBookmarksHorizontalOverflow();
        }

        e.Handled = true;
    }

    private static bool TryOpenBookmarkUrl(string url, out string errorMessage)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = UiStrings.ErrorInvalidBookmarkUrl;
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            errorMessage = UiStrings.ErrorUnsupportedBookmarkUrlScheme;
            return false;
        }

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
            var moveResult = viewModel.MoveItemToFolderStart(draggedItem, _activeDropTargetFolder);
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
        _pressedTreeItem = null;
        _pressedFolderClickCandidate = null;
        e.Pointer.Capture(null);
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

        var viewportHeight = BookmarksScrollViewer.Viewport.Height;
        if (viewportHeight <= 0)
            return;

        var distanceFromTop = _lastTreeDragPoint.Y;
        var distanceFromBottom = viewportHeight - _lastTreeDragPoint.Y;
        var verticalStep = 0d;

        if (distanceFromTop < DragAutoScrollEdgeSize)
            verticalStep = -GetAutoScrollStep(distanceFromTop);
        else if (distanceFromBottom < DragAutoScrollEdgeSize)
            verticalStep = GetAutoScrollStep(distanceFromBottom);

        if (verticalStep == 0)
            return;

        var currentOffset = BookmarksScrollViewer.Offset;
        BookmarksScrollViewer.Offset = new Vector(
            currentOffset.X,
            ClampOffset(
                currentOffset.Y + verticalStep,
                BookmarksScrollViewer.Extent.Height,
                BookmarksScrollViewer.Viewport.Height));
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
