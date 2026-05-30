using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexFuzzyTests
{
    [Fact]
    public void SearchFindsTitleWithTypo()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avlaonia");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("title", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsMediumTokenWithSingleTypo()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "GitHub", "https://github.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("githb");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void SearchDoesNotFuzzyMatchVeryShortTokens()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "C language", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("b");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchRanksExactMatchAboveFuzzyMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("exact", "Avalon", "https://example.com/"),
            new BookmarkSearchDocument("fuzzy", "Avalonia", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalon");

        Assert.Equal("exact", results[0].Id);
    }

    [Fact]
    public void SearchFindsUrlPathWithTypo()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Docs", "https://learn.microsoft.com/dotnet/csharp/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("cshrap");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("url_path_parts", result.MatchedFields);
    }

    [Fact]
    public void AddOrUpdateRemovesOldFuzzyTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Blazor Documentation", "https://example.com/"));

        Assert.Empty(index.Search("avlaonia"));

        BookmarkSearchResult result = Assert.Single(index.Search("blazr"));
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void RemoveRemovesOldFuzzyTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        bool removed = index.Remove("1");

        Assert.True(removed);
        Assert.Empty(index.Search("avlaonia"));
    }

    [Fact]
    public void RebuildWithDuplicateIdsRemovesOldFuzzyTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"),
            new BookmarkSearchDocument("1", "Blazor Documentation", "https://example.com/")
        ]);

        Assert.Empty(index.Search("avlaonia"));

        BookmarkSearchResult result = Assert.Single(index.Search("blazr"));
        Assert.Equal("1", result.Id);
    }
}
