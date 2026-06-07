using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class UnlockSecretsDialog : Window
{
    private readonly Func<string, bool> _tryUnlock;

    public UnlockSecretsDialog()
        : this(_ => false)
    {
    }

    public UnlockSecretsDialog(Func<string, bool> tryUnlock)
    {
        ArgumentNullException.ThrowIfNull(tryUnlock);

        InitializeComponent();
        _tryUnlock = tryUnlock;
        Opened += OnOpened;
    }

    public UnlockSecretsDialogResult? Result { get; private set; }

    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => PasswordTextBox.Focus());
    }

    private void OnUnlockClick(object? sender, RoutedEventArgs e)
    {
        var password = PasswordTextBox.Text ?? string.Empty;

        if (password.Length == 0)
        {
            ShowError(UiStrings.UnlockSecretsPasswordRequired);
            return;
        }

        if (!_tryUnlock(password))
        {
            PasswordTextBox.Clear();
            ShowError(UiStrings.UnlockSecretsFailedMessage);
            return;
        }

        Result = new UnlockSecretsDialogResult(WasUnlocked: true);
        Close(Result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnUnlockClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void ShowError(string message)
    {
        ErrorBanner.ShowError(message);
    }
}
