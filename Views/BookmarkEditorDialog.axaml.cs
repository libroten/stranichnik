using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stranichnik.Localization;
using Stranichnik.Opening;

namespace Stranichnik.Views;

public sealed partial class BookmarkEditorDialog : Window
{
    private static readonly TimeSpan MetadataFetchDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly BookmarkMetadataFetcher MetadataFetcher = new();
    private readonly BookmarkEditorDialogMode _mode;
    private readonly DispatcherTimer _metadataFetchDebounceTimer;
    private CancellationTokenSource? _metadataFetchCancellation;
    private int _metadataFetchVersion;
    private string? _suggestedTitle;
    private bool _isConfiguring;

    public BookmarkEditorDialog()
        : this(BookmarkEditorDialogMode.AddBookmark, title: string.Empty, url: string.Empty)
    {
    }

    private BookmarkEditorDialog(BookmarkEditorDialogMode mode, string title, string url)
    {
        _mode = mode;
        _metadataFetchDebounceTimer = new DispatcherTimer
        {
            Interval = MetadataFetchDebounce
        };
        _metadataFetchDebounceTimer.Tick += OnMetadataFetchDebounceTimerTick;

        InitializeComponent();
        ConfigureMode(title, url);
        UpdateTitlePlaceholder();
        Opened += OnOpened;
        Closed += OnClosed;
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
                ErrorTextBlock.Text = UiStrings.ValidationTitleRequired;
                ErrorTextBlock.IsVisible = true;
                return;
            }

            Result = new(title, string.Empty);
            Close(Result);
            return;
        }

        if (url.Length == 0)
        {
            ErrorTextBlock.Text = UiStrings.ValidationUrlRequired;
            ErrorTextBlock.IsVisible = true;
            return;
        }

        Result = new(title.Length == 0 ? url : title, url);
        Close(Result);
    }

    private void OnUrlTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateTitlePlaceholder();

        if (_isConfiguring)
            return;

        QueueMetadataFetch();
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

    private void OnMetadataSuggestionClick(object? sender, RoutedEventArgs e)
    {
        var suggestedTitle = _suggestedTitle;
        if (string.IsNullOrWhiteSpace(suggestedTitle))
            suggestedTitle = MetadataSuggestionTextBlock.Text;

        if (string.IsNullOrWhiteSpace(suggestedTitle))
            return;

        CancelMetadataFetch();
        _suggestedTitle = suggestedTitle;
        TitleTextBox.Text = suggestedTitle;
        TitleTextBox.Focus();
        e.Handled = true;
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
                Title = UiStrings.BookmarkEditorAddBookmarkTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorAddBookmarkTitle;
                SaveButton.Content = UiStrings.CommonAdd;
                break;
            case BookmarkEditorDialogMode.EditBookmark:
                Title = UiStrings.BookmarkEditorEditBookmarkTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorEditBookmarkTitle;
                SaveButton.Content = UiStrings.CommonSave;
                break;
            case BookmarkEditorDialogMode.AddFolder:
                Title = UiStrings.BookmarkEditorAddFolderTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorAddFolderTitle;
                SaveButton.Content = UiStrings.CommonAdd;
                UrlFieldPanel.IsVisible = false;
                break;
            case BookmarkEditorDialogMode.EditFolder:
                Title = UiStrings.BookmarkEditorEditFolderTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorEditFolderTitle;
                SaveButton.Content = UiStrings.CommonSave;
                UrlFieldPanel.IsVisible = false;
                break;
        }

        _isConfiguring = true;
        TitleTextBox.Text = title;
        UrlTextBox.Text = url;
        _isConfiguring = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CancelMetadataFetch();
        _metadataFetchDebounceTimer.Tick -= OnMetadataFetchDebounceTimerTick;
    }

    private async void OnMetadataFetchDebounceTimerTick(object? sender, EventArgs e)
    {
        _metadataFetchDebounceTimer.Stop();
        await FetchMetadataForCurrentUrlAsync();
    }

    private void QueueMetadataFetch()
    {
        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
            return;

        ClearMetadataSuggestion();
        CancelMetadataFetch();

        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (BookmarkUrlNormalizer.TryNormalizeForOpening(url, out _) != BookmarkUrlOpenStatus.Success)
            return;

        _metadataFetchDebounceTimer.Stop();
        _metadataFetchDebounceTimer.Start();
    }

    private async Task FetchMetadataForCurrentUrlAsync()
    {
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (BookmarkUrlNormalizer.TryNormalizeForOpening(url, out _) != BookmarkUrlOpenStatus.Success)
            return;

        var fetchVersion = ++_metadataFetchVersion;
        var fetchCancellation = new CancellationTokenSource();
        _metadataFetchCancellation = fetchCancellation;
        MetadataLoadingIndicator.IsVisible = true;

        try
        {
            var result = await MetadataFetcher.FetchAsync(url, fetchCancellation.Token);
            if (fetchVersion != _metadataFetchVersion)
                return;

            if (result.IsSuccess && result.Metadata?.Title is { Length: > 0 } title)
                ShowMetadataSuggestion(title);
            else
                ClearMetadataSuggestion();
        }
        finally
        {
            if (_metadataFetchCancellation == fetchCancellation)
                _metadataFetchCancellation = null;

            fetchCancellation.Dispose();

            if (fetchVersion == _metadataFetchVersion)
                MetadataLoadingIndicator.IsVisible = false;
        }
    }

    private void ShowMetadataSuggestion(string title)
    {
        _suggestedTitle = title;
        MetadataSuggestionTextBlock.Text = title;
        MetadataSuggestionButton.IsVisible = true;
    }

    private void ClearMetadataSuggestion()
    {
        _suggestedTitle = null;
        MetadataSuggestionTextBlock.Text = string.Empty;
        MetadataSuggestionButton.IsVisible = false;
        MetadataLoadingIndicator.IsVisible = false;
    }

    private void CancelMetadataFetch()
    {
        _metadataFetchDebounceTimer.Stop();
        _metadataFetchVersion++;
        _metadataFetchCancellation?.Cancel();
        _metadataFetchCancellation = null;
        MetadataLoadingIndicator.IsVisible = false;
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
