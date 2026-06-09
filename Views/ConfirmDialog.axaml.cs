using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Localization;

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
            UiStrings.ConfirmDeleteBookmarkTitle,
            UiStrings.ConfirmDeleteBookmarkMessage(title));

        return dialog.ShowDialog<bool>(owner);
    }

    public static Task<bool> ShowDeleteFolder(Window owner, string title)
    {
        var dialog = new ConfirmDialog(
            UiStrings.ConfirmDeleteFolderTitle,
            UiStrings.ConfirmDeleteFolderMessage(title));

        return dialog.ShowDialog<bool>(owner);
    }

    public static Task<bool> ShowResetSecretMasterPassword(Window owner)
    {
        var dialog = new ConfirmDialog(
            UiStrings.ConfirmResetSecretMasterPasswordTitle,
            UiStrings.ConfirmResetSecretMasterPasswordMessage);

        return dialog.ShowDialog<bool>(owner);
    }

    public static Task<bool> ShowResetSecretMasterPasswordFinal(Window owner)
    {
        var dialog = new ConfirmDialog(
            UiStrings.ConfirmResetSecretMasterPasswordFinalTitle,
            UiStrings.ConfirmResetSecretMasterPasswordFinalMessage);

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
