using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexExactTests
{
    [Fact]
    public void SearchFindsExactTitleToken()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("title", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsExactUrlHostToken()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Code", "https://github.com/AvaloniaUI/Avalonia"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("github");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("url_domain_parts", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsExactUrlPathToken()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Docs", "https://learn.microsoft.com/dotnet/csharp/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("csharp");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("url_path_parts", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsTagTokenWhenProvided()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument(
            "1",
            "Docs",
            "https://example.com/",
            Tags: ["framework"]));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("framework");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("tags", result.MatchedFields);
    }

    [Fact]
    public void SearchFindsNoteTokenWhenProvided()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument(
            "1",
            "Docs",
            "https://example.com/",
            Notes: "Remember for architecture"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("architecture");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Contains("notes", result.MatchedFields);
    }

    [Fact]
    public void SearchDoesNotReturnDocumentContent()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("secret-id", "Private Title", "https://private.example/"));

        BookmarkSearchResult result = Assert.Single(index.Search("private"));

        Assert.Equal("secret-id", result.Id);
        Assert.DoesNotContain("Private Title", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private.example", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrUpdateRemovesOldTokens()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Old Title", "https://example.com/"));
        index.AddOrUpdate(new BookmarkSearchDocument("1", "New Title", "https://example.com/"));

        Assert.Empty(index.Search("old"));
        Assert.Single(index.Search("new"));
    }

    [Fact]
    public void RemovePreventsDocumentFromAppearingInFutureResults()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        bool removed = index.Remove("1");

        Assert.True(removed);
        Assert.Empty(index.Search("avalonia"));
    }

    [Fact]
    public void RebuildUsesLastDocumentForDuplicateIds()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("1", "Old Title", "https://example.com/"),
            new BookmarkSearchDocument("1", "New Title", "https://example.com/")
        ]);

        Assert.Empty(index.Search("old"));
        Assert.Single(index.Search("new"));
    }

    [Fact]
    public void SearchUsesDeterministicOrderingForEqualScores()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("b", "Same", "https://example-b.com/"),
            new BookmarkSearchDocument("a", "Same", "https://example-a.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("same");

        Assert.Collection(
            results,
            result => Assert.Equal("a", result.Id),
            result => Assert.Equal("b", result.Id));
    }
}
