using Avalonia.Controls;
using Avalonia.Input;
using Stranichnik.ViewModels;

namespace Stranichnik.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnFolderRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (sender is not Control { DataContext: BookmarkFolderViewModel folder })
            return;

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.SelectItem(folder);

        folder.IsExpanded = !folder.IsExpanded;
        e.Handled = true;
    }

    private void OnBookmarkRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (sender is Control { DataContext: BookmarkViewModel bookmark } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectItem(bookmark);
        }

        e.Handled = true;
    }
}
