using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<BookmarkTreeItemViewModel> Items { get; } = new()
    {
        new BookmarkFolderViewModel("Работа", new BookmarkTreeItemViewModel[]
        {
            new BookmarkViewModel("Avalonia Docs", "https://docs.avaloniaui.net/"),
            new BookmarkViewModel("NuGet", "https://www.nuget.org/")
        }, isExpanded: true),
        new BookmarkViewModel(
            "Очень длинный заголовок закладки для проверки того, как строка ведет себя, когда название занимает намного больше места, чем обычно ожидается в менеджере закладок",
            "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing"),
        new BookmarkFolderViewModel("Разработка", new BookmarkTreeItemViewModel[]
        {
            new BookmarkFolderViewModel("C#", new BookmarkTreeItemViewModel[]
            {
                new BookmarkViewModel(".NET Documentation", "https://learn.microsoft.com/dotnet/"),
                new BookmarkViewModel("C# Guide", "https://learn.microsoft.com/dotnet/csharp/")
            }),
            new BookmarkViewModel("GitHub", "https://github.com/")
        }, isExpanded: true),
        CreateDeepTestFolder(),
        new BookmarkViewModel("OpenAI", "https://openai.com/")
    };

    private static BookmarkFolderViewModel CreateDeepTestFolder()
    {
        var currentItems = new BookmarkTreeItemViewModel[]
        {
            new BookmarkViewModel(
                "Закладка на двадцатом уровне вложенности",
                "https://example.com/deep/nested/bookmark"),
            new BookmarkViewModel(
                "Очень длинный заголовок закладки для проверки того, как строка ведет себя, когда название занимает намного больше места, чем обычно ожидается в менеджере закладок",
                "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing")
        };

        for (var level = 20; level >= 1; level--)
        {
            currentItems = new BookmarkTreeItemViewModel[]
            {
                new BookmarkFolderViewModel(
                    $"Уровень вложенности {level}",
                    currentItems,
                    isExpanded: level == 1)
            };
        }

        return (BookmarkFolderViewModel)currentItems[0];
    }
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    protected BookmarkTreeItemViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}

public sealed partial class BookmarkFolderViewModel : BookmarkTreeItemViewModel
{
    private bool _isExpanded;

    public BookmarkFolderViewModel(
        string title,
        IEnumerable<BookmarkTreeItemViewModel>? children = null,
        bool isExpanded = false)
        : base(title)
    {
        Children = children is null ? new() : new(children);
        _isExpanded = isExpanded;
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
