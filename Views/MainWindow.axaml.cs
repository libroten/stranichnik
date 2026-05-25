using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stranichnik.ViewModels;

namespace Stranichnik.Views;

public partial class MainWindow : Window
{
    private const double TreeIndentWidth = 28;
    private const double OverflowRowWidth = 560;
    private bool _isMiddleButtonPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        BookmarksScrollViewer.SizeChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        UpdateBookmarksHorizontalOverflow();
    }

    private void OnFolderRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (e.Source is Control source && HasButtonAncestor(source))
            return;

        if (sender is not Control { DataContext: BookmarkFolderViewModel folder })
            return;

        folder.IsExpanded = !folder.IsExpanded;
        UpdateBookmarksHorizontalOverflow();
        e.Handled = true;
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
            return;

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
            return;

        StopMiddleButtonPanning(e);
    }

    private void OnBookmarksScrollPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isMiddleButtonPanning = false;
    }

    private void UpdateBookmarksHorizontalOverflow()
    {
        var maxVisibleDepth = DataContext is MainWindowViewModel viewModel
            ? GetMaxVisibleDepth(viewModel.Items, depth: 0)
            : 0;

        var estimatedContentWidth = maxVisibleDepth * TreeIndentWidth + OverflowRowWidth;
        var hasHorizontalOverflow = estimatedContentWidth > BookmarksScrollViewer.Viewport.Width;

        BookmarksScrollViewer.Classes.Set("hasHorizontalOverflow", hasHorizontalOverflow);
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
