using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Stranichnik.Diagnostics;
using Stranichnik.Icons;
using Stranichnik.Localization;
using Stranichnik.Opening;

namespace Stranichnik.Views;

public sealed partial class BookmarkEditorDialog : Window
{
    private const double BookmarkDialogHeight = 560;
    private const double FolderDialogHeight = 420;

    private static readonly TimeSpan MetadataFetchDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly BookmarkMetadataFetcher MetadataFetcher = new();
    private static readonly IconImageProcessor IconImageProcessor = new();
    private static readonly IconProcessingOptions IconProcessingOptions = new();
    private static readonly FilePickerFileType ImageFilePickerType = new(UiStrings.BookmarkEditorIconImageFiles)
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.ico"],
        MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp", "image/x-icon"],
        AppleUniformTypeIdentifiers = ["public.image"]
    };
    private static readonly IReadOnlyList<FilePickerFileType> ImageFilePickerTypes = [ImageFilePickerType];

    private readonly BookmarkEditorDialogMode _mode;
    private readonly Func<BookmarkEditorDialog, Task<bool>>? _ensureSecretEditingAvailable;
    private readonly DispatcherTimer _metadataFetchDebounceTimer;
    private readonly IImage? _currentIconImage;
    private CancellationTokenSource? _metadataFetchCancellation;
    private int _metadataFetchVersion;
    private string? _suggestedTitle;
    private ReadOnlyMemory<byte> _faviconOriginalBytes;
    private ReadOnlyMemory<byte> _uploadedOriginalBytes;
    private BookmarkEditorIconChoice _selectedIconChoice;
    private bool _isConfiguring;

    public BookmarkEditorDialog()
        : this(
            BookmarkEditorDialogMode.AddBookmark,
            title: string.Empty,
            url: string.Empty,
            currentIconImage: null,
            isSecret: false)
    {
    }

    private BookmarkEditorDialog(
        BookmarkEditorDialogMode mode,
        string title,
        string url,
        IImage? currentIconImage,
        bool isSecret,
        Func<BookmarkEditorDialog, Task<bool>>? ensureSecretEditingAvailable = null)
    {
        _mode = mode;
        _ensureSecretEditingAvailable = ensureSecretEditingAvailable;
        _currentIconImage = currentIconImage;
        _metadataFetchDebounceTimer = new DispatcherTimer
        {
            Interval = MetadataFetchDebounce
        };
        _metadataFetchDebounceTimer.Tick += OnMetadataFetchDebounceTimerTick;

        InitializeComponent();
        ConfigureIconChoices();
        ConfigureMode(title, url, isSecret);
        SelectIconChoice(currentIconImage is null
            ? BookmarkEditorIconChoice.Default
            : BookmarkEditorIconChoice.Current);
        UpdateSecretUi();
        UpdateTitlePlaceholder();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public BookmarkEditorDialogResult? Result { get; private set; }

    public static BookmarkEditorDialog AddBookmark(
        Func<BookmarkEditorDialog, Task<bool>>? ensureSecretEditingAvailable = null)
    {
        return new(
            BookmarkEditorDialogMode.AddBookmark,
            title: string.Empty,
            url: string.Empty,
            currentIconImage: null,
            isSecret: false,
            ensureSecretEditingAvailable);
    }

    public static BookmarkEditorDialog EditBookmark(
        string title,
        string url,
        IImage? currentIconImage,
        bool isSecret,
        Func<BookmarkEditorDialog, Task<bool>>? ensureSecretEditingAvailable = null)
    {
        return new(
            BookmarkEditorDialogMode.EditBookmark,
            title,
            url,
            currentIconImage,
            isSecret,
            ensureSecretEditingAvailable);
    }

    public static BookmarkEditorDialog AddFolder()
    {
        return new(
            BookmarkEditorDialogMode.AddFolder,
            title: string.Empty,
            url: string.Empty,
            currentIconImage: null,
            isSecret: false);
    }

    public static BookmarkEditorDialog EditFolder(string title, IImage? currentIconImage)
    {
        return new(BookmarkEditorDialogMode.EditFolder, title, url: string.Empty, currentIconImage, isSecret: false);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text?.Trim() ?? string.Empty;
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;

        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
        {
            if (title.Length == 0)
            {
                ErrorBanner.ShowError(UiStrings.ValidationTitleRequired);
                return;
            }

            Result = new(title, string.Empty, CreateIconSelection(), IsSecret: false);
            Close(Result);
            return;
        }

        if (url.Length == 0)
        {
            ErrorBanner.ShowError(UiStrings.ValidationUrlRequired);
            return;
        }

        var isSecret = SecretCheckBox.IsChecked == true;
        Result = new(
            title.Length == 0 ? url : title,
            url,
            isSecret ? BookmarkIconSelection.UseDefault : CreateIconSelection(),
            isSecret);
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

        if (_mode is BookmarkEditorDialogMode.AddBookmark or BookmarkEditorDialogMode.EditBookmark)
            QueueMetadataFetch();
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

    private void OnCurrentIconClick(object? sender, RoutedEventArgs e)
    {
        SelectIconChoice(BookmarkEditorIconChoice.Current);
        e.Handled = true;
    }

    private void OnDefaultIconClick(object? sender, RoutedEventArgs e)
    {
        SelectIconChoice(BookmarkEditorIconChoice.Default);
        e.Handled = true;
    }

    private void OnFaviconIconClick(object? sender, RoutedEventArgs e)
    {
        SelectIconChoice(BookmarkEditorIconChoice.Favicon);
        e.Handled = true;
    }

    private void OnUploadedIconClick(object? sender, RoutedEventArgs e)
    {
        SelectIconChoice(BookmarkEditorIconChoice.Uploaded);
        e.Handled = true;
    }

    private async void OnSecretCheckClick(object? sender, RoutedEventArgs e)
    {
        if (SecretCheckBox.IsChecked == true &&
            _ensureSecretEditingAvailable is not null &&
            !await _ensureSecretEditingAvailable(this))
        {
            SecretCheckBox.IsChecked = false;
            ErrorBanner.ShowError(UiStrings.BookmarkEditorSecretModeNotEnabled);
        }
        else
        {
            HideSecretModeWarning();
        }

        UpdateSecretUi();
        e.Handled = true;
    }

    private void HideSecretModeWarning()
    {
        if (!ErrorBanner.IsShowingMessage(UiStrings.BookmarkEditorSecretModeNotEnabled))
            return;

        ErrorBanner.Hide();
    }

    private async void OnUploadIconClick(object? sender, RoutedEventArgs e)
    {
        await PickIconFileAsync();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void ConfigureMode(string title, string url, bool isSecret)
    {
        switch (_mode)
        {
            case BookmarkEditorDialogMode.AddBookmark:
                Height = BookmarkDialogHeight;
                Title = UiStrings.BookmarkEditorAddBookmarkTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorAddBookmarkTitle;
                SaveButton.Content = UiStrings.CommonAdd;
                break;
            case BookmarkEditorDialogMode.EditBookmark:
                Height = BookmarkDialogHeight;
                Title = UiStrings.BookmarkEditorEditBookmarkTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorEditBookmarkTitle;
                SaveButton.Content = UiStrings.CommonSave;
                break;
            case BookmarkEditorDialogMode.AddFolder:
                Height = FolderDialogHeight;
                Title = UiStrings.BookmarkEditorAddFolderTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorAddFolderTitle;
                SaveButton.Content = UiStrings.CommonAdd;
                UrlFieldPanel.IsVisible = false;
                SecretCheckBox.IsVisible = false;
                break;
            case BookmarkEditorDialogMode.EditFolder:
                Height = FolderDialogHeight;
                Title = UiStrings.BookmarkEditorEditFolderTitle;
                DialogTitleTextBlock.Text = UiStrings.BookmarkEditorEditFolderTitle;
                SaveButton.Content = UiStrings.CommonSave;
                UrlFieldPanel.IsVisible = false;
                SecretCheckBox.IsVisible = false;
                break;
        }

        SecretCheckBox.IsVisible = _mode is BookmarkEditorDialogMode.AddBookmark or BookmarkEditorDialogMode.EditBookmark;
        SecretCheckBox.IsChecked = isSecret;
        _isConfiguring = true;
        TitleTextBox.Text = title;
        UrlTextBox.Text = url;
        _isConfiguring = false;
    }

    private void UpdateSecretUi()
    {
        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
            return;

        var isSecret = SecretCheckBox.IsChecked == true;
        IconFieldPanel.IsVisible = !isSecret;

        if (isSecret)
            SelectIconChoice(BookmarkEditorIconChoice.Default);
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
        ClearFaviconOption();
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

            if (result.IsSuccess && result.Metadata?.Favicon is { } favicon)
                ShowFaviconOption(favicon);
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

    private void ConfigureIconChoices()
    {
        CurrentIconTextBlock.Text = UiStrings.BookmarkEditorIconCurrent;
        FaviconIconTextBlock.Text = UiStrings.BookmarkEditorIconFavicon;
        DefaultIconTextBlock.Text = UiStrings.BookmarkEditorIconDefault;
        UploadedIconTextBlock.Text = UiStrings.BookmarkEditorIconUploaded;
        UploadIconTextBlock.Text = UiStrings.BookmarkEditorIconUpload;
        var isFolderMode = _mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder;
        DefaultBookmarkIconPreview.IsVisible = !isFolderMode;
        DefaultFolderIconPreview.IsVisible = isFolderMode;

        CurrentIconButton.IsVisible = _currentIconImage is not null;
        CurrentIconImage.Source = _currentIconImage;
    }

    private BookmarkIconSelection CreateIconSelection()
    {
        return _selectedIconChoice switch
        {
            BookmarkEditorIconChoice.Default => _currentIconImage is null
                ? BookmarkIconSelection.KeepExisting
                : BookmarkIconSelection.UseDefault,
            BookmarkEditorIconChoice.Favicon => BookmarkIconSelection.FromOriginalBytes(_faviconOriginalBytes),
            BookmarkEditorIconChoice.Uploaded => BookmarkIconSelection.FromOriginalBytes(_uploadedOriginalBytes),
            _ => BookmarkIconSelection.KeepExisting
        };
    }

    private void SelectIconChoice(BookmarkEditorIconChoice choice)
    {
        if (choice == BookmarkEditorIconChoice.Current && _currentIconImage is null)
            choice = BookmarkEditorIconChoice.Default;

        if (choice == BookmarkEditorIconChoice.Favicon && _faviconOriginalBytes.Length == 0)
            choice = BookmarkEditorIconChoice.Default;

        if (choice == BookmarkEditorIconChoice.Uploaded && _uploadedOriginalBytes.Length == 0)
            choice = BookmarkEditorIconChoice.Default;

        _selectedIconChoice = choice;
        SetSelectedClass(CurrentIconButton, choice == BookmarkEditorIconChoice.Current);
        SetSelectedClass(DefaultIconButton, choice == BookmarkEditorIconChoice.Default);
        SetSelectedClass(FaviconIconButton, choice == BookmarkEditorIconChoice.Favicon);
        SetSelectedClass(UploadedIconButton, choice == BookmarkEditorIconChoice.Uploaded);
    }

    private static void SetSelectedClass(Button button, bool isSelected)
    {
        if (isSelected)
        {
            if (!button.Classes.Contains("selected"))
                button.Classes.Add("selected");

            return;
        }

        button.Classes.Remove("selected");
    }

    private void ShowFaviconOption(BookmarkFetchedIcon favicon)
    {
        if (_mode is BookmarkEditorDialogMode.AddFolder or BookmarkEditorDialogMode.EditFolder)
            return;

        var image = TryCreateBitmap(favicon.Image);
        if (image is null)
            return;

        _faviconOriginalBytes = favicon.OriginalBytes;
        FaviconIconImage.Source = image;
        FaviconIconButton.IsVisible = true;
        Logs.Print("Bookmark editor favicon icon option became available.");
    }

    private void ClearFaviconOption()
    {
        if (_selectedIconChoice == BookmarkEditorIconChoice.Favicon)
        {
            SelectIconChoice(_currentIconImage is null
                ? BookmarkEditorIconChoice.Default
                : BookmarkEditorIconChoice.Current);
        }

        _faviconOriginalBytes = ReadOnlyMemory<byte>.Empty;
        FaviconIconImage.Source = null;
        FaviconIconButton.IsVisible = false;
    }

    private async Task PickIconFileAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            Logs.Print("Bookmark icon file picker unavailable.");
            ErrorBanner.ShowError(UiStrings.BookmarkEditorIconPickerUnavailable);
            return;
        }

        Logs.Print("Bookmark icon file picker opened.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = ImageFilePickerTypes,
            SuggestedFileType = ImageFilePickerType,
            Title = UiStrings.BookmarkEditorIconPickerTitle
        });

        if (files.Count == 0)
        {
            Logs.Print("Bookmark icon file picker cancelled.");
            return;
        }

        try
        {
            var originalBytes = await ReadStorageFileBytesAsync(files[0]);
            var processed = await Task.Run(() => IconImageProcessor.Process(originalBytes));
            var image = TryCreateBitmap(processed);
            if (image is null)
                throw new InvalidOperationException("Selected icon image could not be loaded.");

            _uploadedOriginalBytes = originalBytes;
            UploadedIconImage.Source = image;
            UploadedIconButton.IsVisible = true;
            SelectIconChoice(BookmarkEditorIconChoice.Uploaded);
            ErrorBanner.Hide();
            Logs.Print("Bookmark icon file selected and processed.");
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Bookmark icon file rejected: image processing error.");
            ErrorBanner.ShowError(UiStrings.BookmarkEditorIconFileInvalid);
        }
        catch (ArgumentException)
        {
            Logs.Print("Bookmark icon file rejected: invalid image data.");
            ErrorBanner.ShowError(UiStrings.BookmarkEditorIconFileInvalid);
        }
        catch (IOException)
        {
            Logs.Print("Bookmark icon file rejected: file read error.");
            ErrorBanner.ShowError(UiStrings.BookmarkEditorIconFileInvalid);
        }
        catch (NotSupportedException)
        {
            Logs.Print("Bookmark icon file rejected: image format is not supported.");
            ErrorBanner.ShowError(UiStrings.BookmarkEditorIconFileInvalid);
        }
    }

    private static async Task<byte[]> ReadStorageFileBytesAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        var totalBytes = 0;

        while (true)
        {
            var readBytes = await stream.ReadAsync(bytes);
            if (readBytes == 0)
                break;

            totalBytes += readBytes;
            if (totalBytes > IconProcessingOptions.MaxInputBytes)
            {
                Logs.Print(
                    "Bookmark icon file rejected: input is too large. " +
                    $"Bytes={totalBytes}, MaxBytes={IconProcessingOptions.MaxInputBytes}.");
                throw new InvalidOperationException("Selected icon image is too large.");
            }

            buffer.Write(bytes, 0, readBytes);
        }

        return buffer.ToArray();
    }

    private static Bitmap? TryCreateBitmap(ProcessedIconImage processed)
    {
        try
        {
            using var stream = new MemoryStream(processed.Bytes.ToArray(), writable: false);
            return new Bitmap(stream);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed record BookmarkEditorDialogResult(
    string Title,
    string Url,
    BookmarkIconSelection IconSelection,
    bool IsSecret);

internal enum BookmarkEditorDialogMode
{
    AddBookmark,
    EditBookmark,
    AddFolder,
    EditFolder
}

internal enum BookmarkEditorIconChoice
{
    Current,
    Default,
    Favicon,
    Uploaded
}
