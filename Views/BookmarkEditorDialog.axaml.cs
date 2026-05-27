using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Stranichnik.Views;

public sealed partial class BookmarkEditorDialog : Window
{
    private readonly BookmarkEditorDialogMode _mode;

    public BookmarkEditorDialog()
        : this(BookmarkEditorDialogMode.AddBookmark, title: string.Empty, url: string.Empty)
    {
    }

    private BookmarkEditorDialog(BookmarkEditorDialogMode mode, string title, string url)
    {
        _mode = mode;
        InitializeComponent();
        ConfigureMode(title, url);
        UpdateTitlePlaceholder();
        Opened += OnOpened;
    }

    public BookmarkEditorDialogResult? Result { get; private set; }

    public static BookmarkEditorDialog AddBookmark()
    {
        return new(BookmarkEditorDialogMode.AddBookmark, title: string.Empty, url: string.Empty);
    }

    public static BookmarkEditorDialog EditBookmark(string title, string url)
    {
        return new(BookmarkEditorDialogMode.EditBookmark, title, url);
    }

    public static BookmarkEditorDialog AddFolder()
    {
        return new(BookmarkEditorDialogMode.AddFolder, title: string.Empty, url: string.Empty);
    }

    public static BookmarkEditorDialog EditFolder(string title)
    {
        return new(BookmarkEditorDialogMode.EditFolder, title, url: string.Empty);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text?.Trim() ?? string.Empty;
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;

        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
        {
            if (title.Length == 0)
            {
                ErrorTextBlock.Text = "Заполните заголовок.";
                ErrorTextBlock.IsVisible = true;
                return;
            }

            Result = new(title, string.Empty);
            Close(Result);
            return;
        }

        if (url.Length == 0)
        {
            ErrorTextBlock.Text = "Заполните URL.";
            ErrorTextBlock.IsVisible = true;
            return;
        }

        Result = new(title.Length == 0 ? url : title, url);
        Close(Result);
    }

    private void OnUrlTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateTitlePlaceholder();
    }

    private void UpdateTitlePlaceholder()
    {
        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
            return;

        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        TitleTextBox.PlaceholderText = url;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var focusTarget = _mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder
            ? TitleTextBox
            : UrlTextBox;
        Dispatcher.UIThread.Post(() => focusTarget.Focus());
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

    private void ConfigureMode(string title, string url)
    {
        switch (_mode)
        {
            case BookmarkEditorDialogMode.AddBookmark:
                Title = "Добавить закладку";
                DialogTitleTextBlock.Text = "Добавить закладку";
                SaveButton.Content = "Добавить";
                break;
            case BookmarkEditorDialogMode.EditBookmark:
                Title = "Редактировать закладку";
                DialogTitleTextBlock.Text = "Редактировать закладку";
                SaveButton.Content = "Сохранить";
                break;
            case BookmarkEditorDialogMode.AddFolder:
                Title = "Добавить папку";
                DialogTitleTextBlock.Text = "Добавить папку";
                SaveButton.Content = "Добавить";
                UrlFieldPanel.IsVisible = false;
                break;
            case BookmarkEditorDialogMode.EditFolder:
                Title = "Редактировать папку";
                DialogTitleTextBlock.Text = "Редактировать папку";
                SaveButton.Content = "Сохранить";
                UrlFieldPanel.IsVisible = false;
                break;
        }

        TitleTextBox.Text = title;
        UrlTextBox.Text = url;
    }
}

public sealed record BookmarkEditorDialogResult(string Title, string Url);

internal enum BookmarkEditorDialogMode
{
    AddBookmark,
    EditBookmark,
    AddFolder,
    EditFolder
}
