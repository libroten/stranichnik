using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stranichnik.ViewModels;

public static class SampleBookmarksFactory
{
    public static ObservableCollection<BookmarkTreeItemViewModel> Create()
    {
        return new()
        {
            new BookmarkFolderViewModel("Work", new BookmarkTreeItemViewModel[]
            {
                new BookmarkViewModel("Avalonia Docs", "https://docs.avaloniaui.net/"),
                new BookmarkViewModel("NuGet", "https://www.nuget.org/")
            }, isExpanded: true),
            new BookmarkViewModel(
                "A very long bookmark title for testing how the row behaves when the title takes much more space than a bookmark manager normally expects",
                "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing"),
            new BookmarkFolderViewModel("Development", new BookmarkTreeItemViewModel[]
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
    }

    private static BookmarkFolderViewModel CreateDeepTestFolder()
    {
        var currentItems = new BookmarkTreeItemViewModel[]
        {
            new BookmarkViewModel(
                "Bookmark at the twentieth nesting level",
                "https://example.com/deep/nested/bookmark"),
            new BookmarkViewModel(
                "A very long bookmark title for testing how the row behaves when the title takes much more space than a bookmark manager normally expects",
                "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing")
        };

        for (var level = 20; level >= 1; level--)
        {
            currentItems = new BookmarkTreeItemViewModel[]
            {
                new BookmarkFolderViewModel(
                    $"Nesting level {level}",
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
                $"Scrolling test bookmark {index}",
                $"https://example.com/scroll-test/{index}"));
        }

        return new BookmarkFolderViewModel(
            "Scrolling test folder",
            bookmarks,
            isExpanded: true);
    }
}
