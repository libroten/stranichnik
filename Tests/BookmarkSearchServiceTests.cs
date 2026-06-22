using System;
using System.Linq;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkSearchServiceTests
{
    [Fact]
    public void RebuildIndexesCurrentSnapshot()
    {
        var service = CreateService();
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateBookmark("avalonia", "Avalonia Docs", "https://docs.avaloniaui.net/"),
            CreateBookmark("github", "GitHub", "https://github.com/"),
        ]);

        service.Rebuild(snapshot);

        var results = service.Search("aval docs");

        Assert.Equal("avalonia", Assert.Single(results).Id);
    }

    [Fact]
    public void AddOrUpdateIndexesNewBookmarkRecords()
    {
        var service = CreateService();
        service.Rebuild(new BookmarkTreeSnapshot([]));

        service.AddOrUpdate(CreateBookmark("github", "GitHub", "https://github.com/"));

        var results = service.Search("github");

        Assert.Equal("github", Assert.Single(results).Id);
    }

    [Fact]
    public void AddOrUpdateRemovesRecordsThatAreNoLongerIndexable()
    {
        var service = CreateService();
        service.Rebuild(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", "Visible", "https://visible.example.com"),
        ]));

        service.AddOrUpdate(CreateBookmark("bookmark", "Visible", "https://visible.example.com") with
        {
            IsSecret = true,
            Title = null,
            Url = null,
        });

        Assert.Empty(service.Search("visible"));
    }

    [Fact]
    public void RemoveRemovesDeletedBookmarkIds()
    {
        var service = CreateService();
        service.Rebuild(new BookmarkTreeSnapshot(
        [
            CreateBookmark("bookmark", "Visible", "https://visible.example.com"),
        ]));

        Assert.True(service.Remove("bookmark"));

        Assert.Empty(service.Search("visible"));
    }

    private static BookmarkSearchService CreateService()
    {
        return new(new InMemoryBookmarkSearchIndex());
    }

    private static BookmarkItemRecord CreateBookmark(
        string id,
        string? title,
        string? url)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            title,
            url,
            IsSecret: false,
            EncryptedPayload: null,
            SecretGenerationId: null,
            CreateMetadata());
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
            "test-device");
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
