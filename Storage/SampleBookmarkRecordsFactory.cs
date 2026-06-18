using System;
using System.Collections.Generic;

namespace Stranichnik.Storage;

public static class SampleBookmarkRecordsFactory
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static BookmarkTreeSnapshot Create()
    {
        var builder = new Builder();

        builder.AddFolder("work", parentId: null, "Work");
        builder.AddBookmark("avalonia-docs", "work", "Avalonia Docs", "https://docs.avaloniaui.net/");
        builder.AddBookmark("nuget", "work", "NuGet", "https://www.nuget.org/");

        builder.AddBookmark(
            "long-root-bookmark",
            parentId: null,
            "A very long bookmark title for testing how the row behaves when the title takes much more space than a bookmark manager normally expects",
            "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing");

        builder.AddFolder("development", parentId: null, "Development");
        builder.AddFolder("csharp", "development", "C#");
        builder.AddBookmark("dotnet-docs", "csharp", ".NET Documentation", "https://learn.microsoft.com/dotnet/");
        builder.AddBookmark("csharp-guide", "csharp", "C# Guide", "https://learn.microsoft.com/dotnet/csharp/");
        builder.AddBookmark("github", "development", "GitHub", "https://github.com/");

        AddDeepTestFolder(builder);

        builder.AddBookmark("openai", parentId: null, "OpenAI", "https://openai.com/");

        AddScrollTestFolder(builder);

        return new(builder.Items);
    }

    private static void AddDeepTestFolder(Builder builder)
    {
        string? parentId = null;

        for (var level = 1; level <= 20; level++)
        {
            var folderId = $"deep-level-{level}";
            builder.AddFolder(folderId, parentId, $"Nesting level {level}");
            parentId = folderId;
        }

        builder.AddBookmark(
            "deep-level-20-bookmark",
            parentId,
            "Bookmark at the twentieth nesting level",
            "https://example.com/deep/nested/bookmark");

        builder.AddBookmark(
            "deep-level-20-long-bookmark",
            parentId,
            "A very long bookmark title for testing how the row behaves when the title takes much more space than a bookmark manager normally expects",
            "https://example.com/articles/very/long/path/with/many/segments/and-query-parameters?utm_source=stranichnik&utm_medium=ui-test&utm_campaign=long-url-case&title=very-long-bookmark-url-for-layout-testing");
    }

    private static void AddScrollTestFolder(Builder builder)
    {
        builder.AddFolder("scroll-test", parentId: null, "Scrolling test folder");

        for (var index = 1; index <= 15; index++)
        {
            builder.AddBookmark(
                $"scroll-test-bookmark-{index}",
                "scroll-test",
                $"Scrolling test bookmark {index}",
                $"https://example.com/scroll-test/{index}");
        }
    }

    private sealed class Builder
    {
        private readonly Dictionary<string, long> _nextSortOrdersByParentId = new(StringComparer.Ordinal);

        public List<BookmarkItemRecord> Items { get; } = [];

        public void AddFolder(string id, string? parentId, string title)
        {
            Items.Add(new(
                id,
                parentId,
                BookmarkItemKind.Folder,
                AllocateSortOrder(parentId),
                title,
                Url: null,
                IsSecret: false,
                EncryptedPayload: null,
                CreateMetadata()));
        }

        public void AddBookmark(string id, string? parentId, string title, string url)
        {
            Items.Add(new(
                id,
                parentId,
                BookmarkItemKind.Bookmark,
                AllocateSortOrder(parentId),
                title,
                url,
                IsSecret: false,
                EncryptedPayload: null,
                CreateMetadata()));
        }

        private long AllocateSortOrder(string? parentId)
        {
            var key = parentId ?? string.Empty;
            var sortOrder = _nextSortOrdersByParentId.GetValueOrDefault(key);
            _nextSortOrdersByParentId[key] = sortOrder - 1000;
            return sortOrder;
        }
    }

    private static BookmarkItemMetadata CreateMetadata()
    {
        return new(
            CreatedAt,
            CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ContentHash: null,
            "sample-data");
    }
}
