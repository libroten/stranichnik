using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly Func<Window, string, Task<SettingsDialogResult>> _saveSecretPassword;

    public SettingsDialog()
        : this((_, _) => Task.FromResult(SettingsDialogResult.Failed(string.Empty)))
    {
    }

    public SettingsDialog(Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword)
    {
        ArgumentNullException.ThrowIfNull(saveSecretPassword);

        InitializeComponent();
        _saveSecretPassword = saveSecretPassword;
        Opened += OnOpened;
    }

    public static void Open(Window owner, Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword)
    {
        var dialog = new SettingsDialog(saveSecretPassword);
        dialog.Show(owner);
    }

    public void ShowSecretPasswordError(string message)
    {
        SecretPasswordStatusBanner.ShowError(message);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => NewMasterPasswordTextBox.Focus());
    }

    private async void OnSaveSecretPasswordClick(object? sender, RoutedEventArgs e)
    {
        await SaveSecretPasswordAsync();
    }

    private async Task SaveSecretPasswordAsync()
    {
        var newPassword = NewMasterPasswordTextBox.Text ?? string.Empty;
        var repeatedPassword = RepeatMasterPasswordTextBox.Text ?? string.Empty;

        if (newPassword.Length == 0)
        {
            ShowSecretPasswordError(UiStrings.SettingsSecretPasswordRequired);
            return;
        }

        if (!string.Equals(newPassword, repeatedPassword, StringComparison.Ordinal))
        {
            ShowSecretPasswordError(UiStrings.SettingsSecretPasswordsDoNotMatch);
            return;
        }

        var result = await _saveSecretPassword(this, newPassword);
        if (!result.Succeeded)
        {
            ShowSecretPasswordError(result.ErrorMessage ?? UiStrings.SettingsSecretPasswordSaveFailed);
            return;
        }

        NewMasterPasswordTextBox.Clear();
        RepeatMasterPasswordTextBox.Clear();
        SecretPasswordStatusBanner.ShowSuccess(result.WasCreated
            ? UiStrings.SettingsSecretPasswordCreated
            : UiStrings.SettingsSecretPasswordChanged);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SaveSecretPasswordAsync();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }
}
