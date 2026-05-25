using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private bool _isRootDropPlaceholderVisible;
    private bool _isRootDropPlaceholderActive;

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
        new BookmarkViewModel("OpenAI", "https://openai.com/"),
        CreateScrollTestFolder()
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

    private static BookmarkFolderViewModel CreateScrollTestFolder()
    {
        var bookmarks = new List<BookmarkTreeItemViewModel>();

        for (var index = 1; index <= 15; index++)
        {
            bookmarks.Add(new BookmarkViewModel(
                $"Тестовая закладка для скроллинга {index}",
                $"https://example.com/scroll-test/{index}"));
        }

        return new BookmarkFolderViewModel(
            "Папка для проверки скроллинга",
            bookmarks,
            isExpanded: true);
    }

    public bool IsRootDropPlaceholderVisible
    {
        get => _isRootDropPlaceholderVisible;
        private set => SetProperty(ref _isRootDropPlaceholderVisible, value);
    }

    public bool IsRootDropPlaceholderActive
    {
        get => _isRootDropPlaceholderActive;
        private set => SetProperty(ref _isRootDropPlaceholderActive, value);
    }

    public bool CanMoveItemToFolder(BookmarkTreeItemViewModel item, BookmarkFolderViewModel? targetParent)
    {
        if (item.Parent == targetParent)
            return false;

        if (item == targetParent || IsDescendantOf(targetParent, item))
            return false;

        return GetMutableItems(item.Parent).Contains(item);
    }

    public bool MoveItemToFolderStart(BookmarkTreeItemViewModel item, BookmarkFolderViewModel? targetParent)
    {
        if (!CanMoveItemToFolder(item, targetParent))
            return false;

        var sourceItems = GetMutableItems(item.Parent);
        var targetItems = GetMutableItems(targetParent);

        sourceItems.Remove(item);
        item.Parent = targetParent;
        targetItems.Insert(0, item);

        return true;
    }

    public void ShowDropPlaceholders(BookmarkTreeItemViewModel draggedItem)
    {
        IsRootDropPlaceholderVisible = CanMoveItemToFolder(draggedItem, targetParent: null);
        IsRootDropPlaceholderActive = false;

        foreach (var folder in EnumerateFolders(Items))
        {
            folder.IsDropPlaceholderVisible = folder.IsExpanded && CanMoveItemToFolder(draggedItem, folder);
            folder.IsDropPlaceholderActive = false;
            folder.IsDragHoverTarget = false;
        }
    }

    public void ActivateDropPlaceholder(BookmarkFolderViewModel? targetParent)
    {
        ClearActiveDropPlaceholder();

        IsRootDropPlaceholderActive = targetParent is null && IsRootDropPlaceholderVisible;

        foreach (var folder in EnumerateFolders(Items))
            folder.IsDropPlaceholderActive = folder == targetParent && folder.IsDropPlaceholderVisible;
    }

    public void ClearActiveDropPlaceholder()
    {
        IsRootDropPlaceholderActive = false;

        foreach (var folder in EnumerateFolders(Items))
            folder.IsDropPlaceholderActive = false;
    }

    public void SetDragHoverFolder(BookmarkFolderViewModel? targetFolder)
    {
        foreach (var folder in EnumerateFolders(Items))
            folder.IsDragHoverTarget = folder == targetFolder;
    }

    public void ClearDropPlaceholders()
    {
        IsRootDropPlaceholderVisible = false;
        IsRootDropPlaceholderActive = false;

        foreach (var folder in EnumerateFolders(Items))
        {
            folder.IsDropPlaceholderVisible = false;
            folder.IsDropPlaceholderActive = false;
            folder.IsDragHoverTarget = false;
        }
    }

    private ObservableCollection<BookmarkTreeItemViewModel> GetMutableItems(BookmarkFolderViewModel? parent)
    {
        return parent?.Children ?? Items;
    }

    private static bool IsDescendantOf(BookmarkFolderViewModel? possibleDescendant, BookmarkTreeItemViewModel item)
    {
        var current = possibleDescendant?.Parent;

        while (current is not null)
        {
            if (current == item)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static IEnumerable<BookmarkFolderViewModel> EnumerateFolders(
        IEnumerable<BookmarkTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item is not BookmarkFolderViewModel folder)
                continue;

            yield return folder;

            foreach (var childFolder in EnumerateFolders(folder.Children))
                yield return childFolder;
        }
    }
}

public abstract partial class BookmarkTreeItemViewModel : ViewModelBase
{
    protected BookmarkTreeItemViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public BookmarkFolderViewModel? Parent { get; internal set; }
}

public sealed partial class BookmarkFolderViewModel : BookmarkTreeItemViewModel
{
    private bool _isExpanded;
    private bool _isDropPlaceholderVisible;
    private bool _isDropPlaceholderActive;
    private bool _isDragHoverTarget;

    public BookmarkFolderViewModel(
        string title,
        IEnumerable<BookmarkTreeItemViewModel>? children = null,
        bool isExpanded = false)
        : base(title)
    {
        Children = children is null ? new() : new(children);
        _isExpanded = isExpanded;

        foreach (var child in Children)
            child.Parent = this;
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

    public bool IsDropPlaceholderVisible
    {
        get => _isDropPlaceholderVisible;
        set => SetProperty(ref _isDropPlaceholderVisible, value);
    }

    public bool IsDropPlaceholderActive
    {
        get => _isDropPlaceholderActive;
        set => SetProperty(ref _isDropPlaceholderActive, value);
    }

    public bool IsDragHoverTarget
    {
        get => _isDragHoverTarget;
        set => SetProperty(ref _isDragHoverTarget, value);
    }
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
