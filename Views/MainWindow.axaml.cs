using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stranichnik.ViewModels;

namespace Stranichnik.Views;

public partial class MainWindow : Window
{
    private const double TreeIndentWidth = 28;
    private const double OverflowRowWidth = 560;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        BookmarksScrollViewer.SizeChanged += (_, _) => UpdateBookmarksHorizontalOverflow();
        UpdateBookmarksHorizontalOverflow();
    }

    private void OnFolderRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control source && HasButtonAncestor(source))
            return;

        if (sender is not Control { DataContext: BookmarkFolderViewModel folder })
            return;

        folder.IsExpanded = !folder.IsExpanded;
        UpdateBookmarksHorizontalOverflow();
        e.Handled = true;
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
