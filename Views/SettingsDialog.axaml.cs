using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Stranichnik.Diagnostics;
using Stranichnik.Localization;
using Stranichnik.Settings;
using Stranichnik.Sync;
using Stranichnik.Sync.Credentials;

namespace Stranichnik.Views;

public sealed partial class SettingsDialog : Window
{
    private static readonly TimeSpan SyncActivityStatusAnimationInterval = TimeSpan.FromMilliseconds(450);

    private readonly Func<Window, string, Task<SettingsDialogResult>> _saveSecretPassword;
    private readonly Func<Window, Task<SettingsDialogResult>> _resetSecretMasterPassword;
    private readonly Func<Window, Task<SettingsDialogResult>> _testSyncConnection;
    private readonly Func<Window, Task<SettingsDialogResult>> _syncNow;
    private readonly Func<IReadOnlyList<SettingsSyncRemoteProblemViewModel>> _loadSyncRemoteProblems;
    private readonly Func<Window, string, Task<SettingsDialogResult>> _clearSyncRemoteProblem;
    private readonly Func<Window, string, Task<SettingsDialogResult>> _deleteSyncRemoteProblem;
    private readonly ISyncCredentialStore _syncCredentialStore;
    private readonly ISyncActivityService _syncActivityService;
    private readonly DispatcherTimer _syncActivityStatusTimer;
    private bool _isSyncActionRunning;
    private int _syncActivityStatusDotCount;

    public SettingsDialog()
        : this(
            (_, _) => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            _ => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            () => [],
            (_, _) => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            (_, _) => Task.FromResult(SettingsDialogResult.Failed(string.Empty)),
            new InMemorySyncCredentialStore(),
            new SyncActivityService())
    {
    }

    public SettingsDialog(
        Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword,
        Func<Window, Task<SettingsDialogResult>> resetSecretMasterPassword,
        Func<Window, Task<SettingsDialogResult>> testSyncConnection,
        Func<Window, Task<SettingsDialogResult>> syncNow,
        Func<IReadOnlyList<SettingsSyncRemoteProblemViewModel>> loadSyncRemoteProblems,
        Func<Window, string, Task<SettingsDialogResult>> clearSyncRemoteProblem,
        Func<Window, string, Task<SettingsDialogResult>> deleteSyncRemoteProblem,
        ISyncCredentialStore syncCredentialStore,
        ISyncActivityService syncActivityService)
    {
        ArgumentNullException.ThrowIfNull(saveSecretPassword);
        ArgumentNullException.ThrowIfNull(resetSecretMasterPassword);
        ArgumentNullException.ThrowIfNull(testSyncConnection);
        ArgumentNullException.ThrowIfNull(syncNow);
        ArgumentNullException.ThrowIfNull(loadSyncRemoteProblems);
        ArgumentNullException.ThrowIfNull(clearSyncRemoteProblem);
        ArgumentNullException.ThrowIfNull(deleteSyncRemoteProblem);
        ArgumentNullException.ThrowIfNull(syncCredentialStore);
        ArgumentNullException.ThrowIfNull(syncActivityService);

        InitializeComponent();
        _saveSecretPassword = saveSecretPassword;
        _resetSecretMasterPassword = resetSecretMasterPassword;
        _testSyncConnection = testSyncConnection;
        _syncNow = syncNow;
        _loadSyncRemoteProblems = loadSyncRemoteProblems;
        _clearSyncRemoteProblem = clearSyncRemoteProblem;
        _deleteSyncRemoteProblem = deleteSyncRemoteProblem;
        _syncCredentialStore = syncCredentialStore;
        _syncActivityService = syncActivityService;
        _syncActivityStatusTimer = new DispatcherTimer
        {
            Interval = SyncActivityStatusAnimationInterval
        };
        _syncActivityStatusTimer.Tick += OnSyncActivityStatusTimerTick;
        Opened += OnOpened;
        Closed += OnClosed;
        _syncActivityService.ActivityChanged += OnSyncActivityChanged;
        LoadSyncSettings();
        RefreshSyncRemoteProblems();
        SetSyncActivityStatus(_syncActivityService.IsActive);
    }

    public static void Open(
        Window owner,
        Func<Window, string, Task<SettingsDialogResult>> saveSecretPassword,
        Func<Window, Task<SettingsDialogResult>> resetSecretMasterPassword,
        Func<Window, Task<SettingsDialogResult>> testSyncConnection,
        Func<Window, Task<SettingsDialogResult>> syncNow,
        Func<IReadOnlyList<SettingsSyncRemoteProblemViewModel>> loadSyncRemoteProblems,
        Func<Window, string, Task<SettingsDialogResult>> clearSyncRemoteProblem,
        Func<Window, string, Task<SettingsDialogResult>> deleteSyncRemoteProblem,
        ISyncCredentialStore syncCredentialStore,
        ISyncActivityService syncActivityService)
    {
        var dialog = new SettingsDialog(
            saveSecretPassword,
            resetSecretMasterPassword,
            testSyncConnection,
            syncNow,
            loadSyncRemoteProblems,
            clearSyncRemoteProblem,
            deleteSyncRemoteProblem,
            syncCredentialStore,
            syncActivityService);
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

    private void OnClosed(object? sender, EventArgs e)
    {
        _syncActivityService.ActivityChanged -= OnSyncActivityChanged;
        _syncActivityStatusTimer.Stop();
    }

    private void OnSyncActivityChanged(object? sender, SyncActivityChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => SetSyncActivityStatus(e.IsActive));
    }

    private void SetSyncActivityStatus(bool isActive)
    {
        if (!isActive)
        {
            _syncActivityStatusTimer.Stop();
            if (SyncStatusBanner.IsShowingInfo())
                SyncStatusBanner.Hide();

            return;
        }

        _syncActivityStatusDotCount = 1;
        ShowSyncActivityStatus();
        _syncActivityStatusTimer.Start();
    }

    private void OnSyncActivityStatusTimerTick(object? sender, EventArgs e)
    {
        _syncActivityStatusDotCount = (_syncActivityStatusDotCount + 1) % 4;
        ShowSyncActivityStatus();
    }

    private void ShowSyncActivityStatus()
    {
        SyncStatusBanner.ShowInfo(
            UiStrings.SettingsSyncInProgress + new string('.', _syncActivityStatusDotCount));
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
        ClearSyncStatus();
        SaveSyncSettings();
        SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncSaved);
    }

    private async void OnTestSyncConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (_isSyncActionRunning)
            return;

        ClearSyncStatus();
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

        ClearSyncStatus();
        SaveSyncSettings();

        try
        {
            _isSyncActionRunning = true;
            var result = await _syncNow(this);
            if (result.Succeeded)
            {
                SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncNowSucceeded);
                RefreshSyncRemoteProblems();
                return;
            }

            SyncStatusBanner.ShowError(result.ErrorMessage ?? UiStrings.SettingsSyncNowFailed);
            RefreshSyncRemoteProblems();
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

    private async void OnClearSyncRemoteProblemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string problemId } || _isSyncActionRunning)
            return;

        ClearSyncStatus();

        try
        {
            _isSyncActionRunning = true;
            var result = await _clearSyncRemoteProblem(this, problemId);
            if (result.Succeeded)
                SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncRemoteProblemCleared);
            else
                SyncStatusBanner.ShowError(result.ErrorMessage ?? UiStrings.SettingsSyncRemoteProblemClearFailed);
        }
        finally
        {
            _isSyncActionRunning = false;
            RefreshSyncRemoteProblems();
        }
    }

    private async void OnDeleteSyncRemoteProblemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string problemId } || _isSyncActionRunning)
            return;

        if (!await ConfirmDialog.ShowDeleteSyncRemoteProblem(this))
            return;

        ClearSyncStatus();
        SaveSyncSettings();

        try
        {
            _isSyncActionRunning = true;
            var result = await _deleteSyncRemoteProblem(this, problemId);
            if (result.Succeeded)
                SyncStatusBanner.ShowSuccess(UiStrings.SettingsSyncRemoteProblemDeleted);
            else
                SyncStatusBanner.ShowError(result.ErrorMessage ?? UiStrings.SettingsSyncRemoteProblemDeleteFailed);
        }
        finally
        {
            _isSyncActionRunning = false;
            RefreshSyncRemoteProblems();
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

    private void ClearSyncStatus()
    {
        SyncStatusBanner.Hide();
    }

    private void RefreshSyncRemoteProblems()
    {
        SyncRemoteProblemsList.Children.Clear();

        var problems = _loadSyncRemoteProblems();
        SyncRemoteProblemsSection.IsVisible = problems.Count > 0;

        foreach (var problem in problems)
            SyncRemoteProblemsList.Children.Add(CreateSyncRemoteProblemCard(problem));
    }

    private Border CreateSyncRemoteProblemCard(SettingsSyncRemoteProblemViewModel problem)
    {
        var pathTextBlock = new TextBlock
        {
            Text = $"{UiStrings.SettingsSyncRemoteProblemFileLabel} {problem.RelativePath}",
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetBrush("DialogTextPrimaryBrush", Brushes.Black),
            FontFamily = new FontFamily("Consolas, Menlo, Monaco, Courier New"),
            FontSize = 13
        };
        ToolTip.SetTip(pathTextBlock, problem.RelativePath);

        var detailsTextBlock = new TextBlock
        {
            Text = UiStrings.SettingsSyncRemoteProblemDetails(problem.ReasonCode, problem.SeenCount),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetBrush("DialogTextSecondaryBrush", Brushes.Gray),
            FontSize = 12
        };

        var clearButton = CreateRemoteProblemButton(
            UiStrings.SettingsSyncRemoteProblemClearShortAction,
            UiStrings.SettingsSyncRemoteProblemClearAction,
            problem.Id,
            OnClearSyncRemoteProblemClick);
        var deleteButton = CreateRemoteProblemButton(
            UiStrings.SettingsSyncRemoteProblemDeleteShortAction,
            UiStrings.SettingsSyncRemoteProblemDeleteAction,
            problem.Id,
            OnDeleteSyncRemoteProblemClick);
        deleteButton.Classes.Add("danger");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Children =
            {
                clearButton,
                deleteButton
            }
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        content.Children.Add(new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                pathTextBlock,
                detailsTextBlock
            }
        });
        Grid.SetColumn(buttons, 1);
        content.Children.Add(buttons);

        return new Border
        {
            Padding = new Avalonia.Thickness(8, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = Brushes.Transparent,
            Child = content
        };
    }

    private static Button CreateRemoteProblemButton(
        string text,
        string tooltip,
        string problemId,
        EventHandler<RoutedEventArgs> clickHandler)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            },
            Tag = problemId,
            MinWidth = 84,
            Height = 28,
            Padding = new Avalonia.Thickness(9, 4),
            FontSize = 12,
            Classes = { "dialogButton" }
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += clickHandler;
        return button;
    }

    private IBrush GetBrush(string key, IBrush fallback)
    {
        if (TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
            return brush;

        return Application.Current?.TryGetResource(key, ActualThemeVariant, out resource) == true &&
            resource is IBrush appBrush
            ? appBrush
            : fallback;
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
