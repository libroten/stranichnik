using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexPrefixTests
{
    [Fact]
    public void SearchFindsTitlePrefixMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("aval");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("title", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsDomainPrefixMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Docs", "https://learn.microsoft.com/dotnet/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("micro");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("url_domain_parts", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsPathPrefixMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Docs", "https://example.com/dotnet/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("dot");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("url_path_parts", result.MatchedFields);
    }

    [Fact]
    public void SearchDoesNotUsePrefixForVeryShortQueryToken()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("av");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchRanksExactMatchAbovePrefixOnlyMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("exact", "Aval", "https://example.com/"),
            new BookmarkSearchDocument("prefix", "Avalonia", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("aval");

        Assert.Equal("exact", results[0].Id);
    }

    [Fact]
    public void SearchReturnsDeterministicPrefixResults()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("b", "Avalanche", "https://example.com/"),
            new BookmarkSearchDocument("a", "Avalonia", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> first = index.Search("aval");
        IReadOnlyList<BookmarkSearchResult> second = index.Search("aval");

        Assert.Collection(
            second,
            result => Assert.Equal(first[0].Id, result.Id),
            result => Assert.Equal(first[1].Id, result.Id));
    }

    [Fact]
    public void AddOrUpdateRemovesOldPrefixTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Blazor Documentation", "https://example.com/"));

        Assert.Empty(index.Search("aval"));

        BookmarkSearchResult result = Assert.Single(index.Search("blaz"));
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void RemoveRemovesOldPrefixTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        bool removed = index.Remove("1");

        Assert.True(removed);
        Assert.Empty(index.Search("aval"));
    }

    [Fact]
    public void RebuildWithDuplicateIdsRemovesOldPrefixTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"),
            new BookmarkSearchDocument("1", "Blazor Documentation", "https://example.com/")
        ]);

        Assert.Empty(index.Search("aval"));

        BookmarkSearchResult result = Assert.Single(index.Search("blaz"));
        Assert.Equal("1", result.Id);
    }
}
