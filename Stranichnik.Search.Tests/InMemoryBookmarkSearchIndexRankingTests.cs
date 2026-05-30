using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexRankingTests
{
    [Fact]
    public void SearchRanksTitleMatchAboveUrlFallbackMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("title", "Avalonia Guide", "https://example.com/docs"),
            new BookmarkSearchDocument("url", "Framework Guide", "https://example.com/avalonia")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia");

        Assert.Equal("title", results[0].Id);
    }

    [Fact]
    public void SearchRanksHostDomainMatchHigh()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("github", "Source Hosting", "https://github.com/AvaloniaUI/Avalonia"),
            new BookmarkSearchDocument("title", "GitHub Notes", "https://example.com/notes")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("github");

        Assert.Equal("github", results[0].Id);
    }

    [Fact]
    public void SearchRanksAllTermMatchAboveOneTermMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("both", "Avalonia Documentation", "https://example.com/"),
            new BookmarkSearchDocument("one", "Avalonia", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia documentation");

        Assert.Equal("both", results[0].Id);
    }

    [Fact]
    public void SearchAppliesMinimumScore()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        BookmarkSearchOptions options = new()
        {
            MinimumScore = double.MaxValue
        };

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia", options);

        Assert.Empty(results);
    }

    [Fact]
    public void SearchRespectsMaximumResultCount()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("1", "Avalonia One", "https://example.com/1"),
            new BookmarkSearchDocument("2", "Avalonia Two", "https://example.com/2")
        ]);
        BookmarkSearchOptions options = new()
        {
            MaxResults = 1
        };

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia", options);

        Assert.Single(results);
    }
}
