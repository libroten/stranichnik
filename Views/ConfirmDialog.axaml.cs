using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stranichnik.Views;

public sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private ConfirmDialog(string title, string message)
        : this()
    {
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    public static Task<bool> ShowDeleteBookmark(Window owner, string title)
    {
        var dialog = new ConfirmDialog(
            "Удалить закладку",
            $"Вы точно хотите удалить закладку {title}?");

        return dialog.ShowDialog<bool>(owner);
    }

    public static Task<bool> ShowDeleteFolder(Window owner, string title)
    {
        var dialog = new ConfirmDialog(
            "Удалить папку",
            $"Вы точно хотите удалить папку {title}? Будет удалено все ее содержимое.");

        return dialog.ShowDialog<bool>(owner);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close(false);
        e.Handled = true;
    }
}
