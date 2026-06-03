using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Localization;
using Stranichnik.Theming;

namespace Stranichnik.Views;

public sealed partial class AppearanceDialog : Window
{
    private ThemeMode _selectedTheme;

    public AppearanceDialog()
        : this(ThemeService.CurrentTheme)
    {
    }

    private AppearanceDialog(ThemeMode currentTheme)
    {
        _selectedTheme = currentTheme;
        InitializeComponent();
        Title = UiStrings.AppearanceDialogTitle;
        UpdateSelection();
        Opened += OnOpened;
    }

    public static Task<ThemeMode?> Show(Window owner, ThemeMode currentTheme)
    {
        var dialog = new AppearanceDialog(currentTheme);

        return dialog.ShowDialog<ThemeMode?>(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var focusTarget = _selectedTheme == ThemeMode.Dark
            ? DarkButton
            : LightButton;
        focusTarget.Focus();
    }

    private void OnLightClick(object? sender, RoutedEventArgs e)
    {
        SelectTheme(ThemeMode.Light);
        e.Handled = true;
    }

    private void OnDarkClick(object? sender, RoutedEventArgs e)
    {
        SelectTheme(ThemeMode.Dark);
        e.Handled = true;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Close(_selectedTheme);
        e.Handled = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void SelectTheme(ThemeMode theme)
    {
        _selectedTheme = theme;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        SetSelected(LightButton, _selectedTheme == ThemeMode.Light);
        SetSelected(DarkButton, _selectedTheme == ThemeMode.Dark);
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
