using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private BookmarkTreeItemViewModel? _selectedItem;

    public ObservableCollection<BookmarkTreeItemViewModel> Items { get; } = new()
    {
        new BookmarkFolderViewModel("Работа", new BookmarkTreeItemViewModel[]
        {
            new BookmarkViewModel("Avalonia Docs", "https://docs.avaloniaui.net/"),
            new BookmarkViewModel("NuGet", "https://www.nuget.org/")
        }),
        new BookmarkFolderViewModel("Разработка", new BookmarkTreeItemViewModel[]
        {
            new BookmarkFolderViewModel("C#", new BookmarkTreeItemViewModel[]
            {
                new BookmarkViewModel(".NET Documentation", "https://learn.microsoft.com/dotnet/"),
                new BookmarkViewModel("C# Guide", "https://learn.microsoft.com/dotnet/csharp/")
            }),
            new BookmarkViewModel("GitHub", "https://github.com/")
        }),
        new BookmarkViewModel("OpenAI", "https://openai.com/")
    };

    public void SelectItem(BookmarkTreeItemViewModel item)
    {
        if (_selectedItem == item)
            return;

        if (_selectedItem is not null)
            _selectedItem.IsSelected = false;

        _selectedItem = item;
        _selectedItem.IsSelected = true;
    }
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    private bool _isSelected;

    protected BookmarkTreeItemViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed partial class BookmarkFolderViewModel : BookmarkTreeItemViewModel
{
    private bool _isExpanded = true;

    public BookmarkFolderViewModel(string title, IEnumerable<BookmarkTreeItemViewModel>? children = null)
        : base(title)
    {
        Children = children is null ? new() : new(children);
    }

    public ObservableCollection<BookmarkTreeItemViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                OnPropertyChanged(nameof(ExpansionGlyph));
        }
    }

    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
}

public sealed partial class BookmarkViewModel : BookmarkTreeItemViewModel
{
    public BookmarkViewModel(string title, string url)
        : base(title)
    {
        Url = url;
    }

    public string Url { get; }
}
