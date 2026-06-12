using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Icons;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class IconLibraryDialog : Window
{
    public IconLibraryDialog()
        : this([])
    {
    }

    public IconLibraryDialog(IReadOnlyList<IconLibraryItem> items)
    {
        InitializeComponent();
        Title = UiStrings.IconLibraryTitle;
        ItemsControl.ItemsSource = items;
        EmptyTextBlock.IsVisible = items.Count == 0;
    }

    public IconLibraryDialogResult? Result { get; private set; }

    public static Task<IconLibraryDialogResult?> ShowAsync(
        Window owner,
        IReadOnlyList<IconLibraryItem> items)
    {
        var dialog = new IconLibraryDialog(items);
        return dialog.ShowDialog<IconLibraryDialogResult?>(owner);
    }

    private void OnIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IconLibraryItem item })
            return;

        Result = new IconLibraryDialogResult(item.Selection, item.Preview);
        Close(Result);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }
}
