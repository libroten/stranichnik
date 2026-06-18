using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stranichnik.Diagnostics;
using Stranichnik.Localization;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;

namespace Stranichnik.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly Func<Window, string, Task<SettingsDialogResult>> _saveSecretPassword;
    private readonly Func<Window, Task<SettingsDialogResult>> _resetSecretMasterPassword;
    private readonly Func<Window, Task<SettingsDialogResult>> _testSyncConnection;
    private readonly Func<Window, Task<SettingsDialogResult>> _syncNow;
    private readonly ISyncCredentialStore _syncCredentialStore;
    private bool _isSyncActionRunning;

    public SettingsDialog()
        : this(
            (_, _) => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            new InMemorySyncCredentialStore())
    {
    }

    public SettingsDialog(
        Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword,
        Func<Window, Task<SettingsDialogResult>> resetSecretMasterPassword,
        Func<Window, Task<SettingsDialogResult>> testSyncConnection,
        Func<Window, Task<SettingsDialogResult>> syncNow,
        ISyncCredentialStore syncCredentialStore)
    {
        ArgumentNullException.ThrowIfNull(saveSecretPassword);
        ArgumentNullException.ThrowIfNull(resetSecretMasterPassword);
        ArgumentNullException.ThrowIfNull(testSyncConnection);
        ArgumentNullException.ThrowIfNull(syncNow);
        ArgumentNullException.ThrowIfNull(syncCredentialStore);

        InitializeComponent();
        _saveSecretPassword = saveSecretPassword;
        _resetSecretMasterPassword = resetSecretMasterPassword;
        _testSyncConnection = testSyncConnection;
        _syncNow = syncNow;
        _syncCredentialStore = syncCredentialStore;
        Opened += OnOpened;
        LoadSyncSettings();
    }

    public static void Open(
        Window owner,
        Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword,
        Func<Window, Task<SettingsDialogResult>> resetSecretMasterPassword,
        Func<Window, Task<SettingsDialogResult>> testSyncConnection,
        Func<Window, Task<SettingsDialogResult>> syncNow,
        ISyncCredentialStore syncCredentialStore)
    {
        var dialog = new SettingsDialog(
            saveSecretPassword,
            resetSecretMasterPassword,
            testSyncConnection,
            syncNow,
            syncCredentialStore);
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

    private async void OnResetSecretMasterPasswordClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.ShowResetSecretMasterPassword(this))
            return;

        if (!await ConfirmDialog.ShowResetSecretMasterPasswordFinal(this))
            return;

        var result = await _resetSecretMasterPassword(this);
        if (!result.Succeeded)
        {
            ShowSecretPasswordError(result.ErrorMessage ?? UiStrings.SettingsSecretResetFailed);
            return;
        }

        NewMasterPasswordTextBox.Clear();
        RepeatMasterPasswordTextBox.Clear();
        SecretPasswordStatusBanner.ShowSuccess(UiStrings.SettingsSecretResetSuccess);
    }

    private void OnSaveSyncSettingsClick(object? sender, RoutedEventArgs e)
    {
        SaveSyncSettings();
        SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncSaved);
    }

    private async void OnTestSyncConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (_isSyncActionRunning)
            return;

        SaveSyncSettings();

        try
        {
            _isSyncActionRunning = true;
            var result = await _testSyncConnection(this);
            if (result.Succeeded)
            {
                SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncConnectionSucceeded);
                return;
            }

            SyncStatusBanner.ShowError(result.ErrorMessage ?? UiStrings.SettingsSyncConnectionFailed);
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Sync connection test failed: unexpected state.");
            SyncStatusBanner.ShowError(UiStrings.SettingsSyncConnectionFailed);
        }
        finally
        {
            _isSyncActionRunning = false;
        }
    }

    private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
    {
        if (_isSyncActionRunning)
            return;

        SaveSyncSettings();

        try
        {
            _isSyncActionRunning = true;
            var result = await _syncNow(this);
            if (result.Succeeded)
            {
                SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncNowSucceeded);
                return;
            }

            SyncStatusBanner.ShowError(result.ErrorMessage ?? UiStrings.SettingsSyncNowFailed);
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Manual sync failed: unexpected state.");
            SyncStatusBanner.ShowError(UiStrings.SettingsSyncNowFailed);
        }
        finally
        {
            _isSyncActionRunning = false;
        }
    }

    private void SaveSyncSettings()
    {
        var settings = AppSettingsService.Load();
        settings.Sync.IsEnabled = SyncEnabledCheckBox.IsChecked == true;
        settings.Sync.WebDavUrl = SyncWebDavUrlTextBox.Text?.Trim() ?? string.Empty;
        settings.Sync.Username = SyncUsernameTextBox.Text?.Trim() ?? string.Empty;
        AppSettingsService.Save(settings);

        var password = SyncPasswordTextBox.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(password))
        {
            _syncCredentialStore.SaveForSession(new SyncCredentials(password));
            SyncPasswordTextBox.Clear();
        }
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

    private void LoadSyncSettings()
    {
        var settings = AppSettingsService.Load();
        SyncEnabledCheckBox.IsChecked = settings.Sync.IsEnabled;
        SyncWebDavUrlTextBox.Text = settings.Sync.WebDavUrl;
        SyncUsernameTextBox.Text = settings.Sync.Username;
        SyncPasswordTextBox.Clear();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (IsSyncInputFocused())
                OnSaveSyncSettingsClick(sender, e);
            else
                await SaveSecretPasswordAsync();

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private bool IsSyncInputFocused()
    {
        return SyncWebDavUrlTextBox.IsFocused ||
            SyncUsernameTextBox.IsFocused ||
            SyncPasswordTextBox.IsFocused;
    }
}
