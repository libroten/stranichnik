using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class SetMasterPasswordDialog : Window
{
    public SetMasterPasswordDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    public SetMasterPasswordDialogResult? Result { get; private set; }

    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => PasswordTextBox.Focus());
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        var password = PasswordTextBox.Text ?? string.Empty;
        var repeatedPassword = RepeatPasswordTextBox.Text ?? string.Empty;

        if (password.Length == 0)
        {
            ShowError(UiStrings.SetMasterPasswordPasswordRequired);
            return;
        }

        if (!string.Equals(password, repeatedPassword, StringComparison.Ordinal))
        {
            ShowError(UiStrings.SetMasterPasswordPasswordsDoNotMatch);
            return;
        }

        Result = new SetMasterPasswordDialogResult(password);
        Close(Result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.IsVisible = true;
    }
}
