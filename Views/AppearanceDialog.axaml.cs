using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Localization;
using Stranichnik.Theming;

namespace Stranichnik.Views;

public sealed partial class AppearanceDialog : Window
{
    private readonly Action<ThemeMode> _applyTheme;
    private ThemeMode _currentTheme;

    public AppearanceDialog()
        : this(ThemeService.CurrentTheme, _ => { })
    {
    }

    private AppearanceDialog(ThemeMode currentTheme, Action<ThemeMode> applyTheme)
    {
        _currentTheme = currentTheme;
        _applyTheme = applyTheme;
        InitializeComponent();
        Title = UiStrings.AppearanceDialogTitle;
        UpdateSelection();
        Opened += OnOpened;
    }

    public static void Open(Window owner, ThemeMode currentTheme, Action<ThemeMode> applyTheme)
    {
        ArgumentNullException.ThrowIfNull(applyTheme);

        var dialog = new AppearanceDialog(currentTheme, applyTheme);
        dialog.Show(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var focusTarget = _currentTheme switch
        {
            ThemeMode.System => SystemButton,
            ThemeMode.Dark => DarkButton,
            _ => LightButton
        };
        focusTarget.Focus();
    }

    private void OnSystemClick(object? sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeMode.System);
        e.Handled = true;
    }

    private void OnLightClick(object? sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeMode.Light);
        e.Handled = true;
    }

    private void OnDarkClick(object? sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeMode.Dark);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void UpdateSelection()
    {
        SetSelected(SystemButton, _currentTheme == ThemeMode.System);
        SetSelected(LightButton, _currentTheme == ThemeMode.Light);
        SetSelected(DarkButton, _currentTheme == ThemeMode.Dark);
    }

    private void ApplyTheme(ThemeMode theme)
    {
        if (_currentTheme == theme)
            return;

        _currentTheme = theme;
        UpdateSelection();
        _applyTheme(theme);
    }

    private static void SetSelected(Button button, bool isSelected)
    {
        if (isSelected)
        {
            if (!button.Classes.Contains("selected"))
                button.Classes.Add("selected");

            return;
        }

        button.Classes.Remove("selected");
    }
}
