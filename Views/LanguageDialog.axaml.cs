using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class LanguageDialog : Window
{
    private readonly Action<string> _applyLanguage;
    private string _currentLanguage;

    public LanguageDialog()
        : this(LanguageService.CurrentLanguage, _ => { })
    {
    }

    private LanguageDialog(string currentLanguage, Action<string> applyLanguage)
    {
        _currentLanguage = LanguageService.NormalizeLanguage(currentLanguage);
        _applyLanguage = applyLanguage;
        InitializeComponent();
        Title = UiStrings.LanguageDialogTitle;
        UpdateSelection();
        Opened += OnOpened;
    }

    public static void Open(Window owner, string currentLanguage, Action<string> applyLanguage)
    {
        ArgumentNullException.ThrowIfNull(applyLanguage);

        var dialog = new LanguageDialog(currentLanguage, applyLanguage);
        dialog.Show(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var focusTarget = _currentLanguage == LanguageService.RussianLanguage
            ? RussianButton
            : EnglishButton;
        focusTarget.Focus();
    }

    private void OnEnglishClick(object? sender, RoutedEventArgs e)
    {
        ApplyLanguage(LanguageService.EnglishLanguage);
        e.Handled = true;
    }

    private void OnRussianClick(object? sender, RoutedEventArgs e)
    {
        ApplyLanguage(LanguageService.RussianLanguage);
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
        SetSelected(EnglishButton, _currentLanguage == LanguageService.EnglishLanguage);
        SetSelected(RussianButton, _currentLanguage == LanguageService.RussianLanguage);
    }

    private void ApplyLanguage(string language)
    {
        var normalizedLanguage = LanguageService.NormalizeLanguage(language);
        if (_currentLanguage == normalizedLanguage)
            return;

        _currentLanguage = normalizedLanguage;
        UpdateSelection();
        _applyLanguage(normalizedLanguage);
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
