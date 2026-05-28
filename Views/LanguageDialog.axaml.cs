using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class LanguageDialog : Window
{
    private string _selectedLanguage;

    public LanguageDialog()
        : this(LanguageService.CurrentLanguage)
    {
    }

    private LanguageDialog(string currentLanguage)
    {
        _selectedLanguage = LanguageService.NormalizeLanguage(currentLanguage);
        InitializeComponent();
        Title = UiStrings.LanguageDialogTitle;
        UpdateSelection();
        Opened += OnOpened;
    }

    public static Task<string?> Show(Window owner, string currentLanguage)
    {
        var dialog = new LanguageDialog(currentLanguage);

        return dialog.ShowDialog<string?>(owner);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var focusTarget = _selectedLanguage == LanguageService.RussianLanguage
            ? RussianButton
            : EnglishButton;
        focusTarget.Focus();
    }

    private void OnEnglishClick(object? sender, RoutedEventArgs e)
    {
        SelectLanguage(LanguageService.EnglishLanguage);
        e.Handled = true;
    }

    private void OnRussianClick(object? sender, RoutedEventArgs e)
    {
        SelectLanguage(LanguageService.RussianLanguage);
        e.Handled = true;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Close(_selectedLanguage);
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

    private void SelectLanguage(string language)
    {
        _selectedLanguage = LanguageService.NormalizeLanguage(language);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        SetSelected(EnglishButton, _selectedLanguage == LanguageService.EnglishLanguage);
        SetSelected(RussianButton, _selectedLanguage == LanguageService.RussianLanguage);
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
