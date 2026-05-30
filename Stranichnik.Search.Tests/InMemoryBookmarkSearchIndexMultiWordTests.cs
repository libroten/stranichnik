using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexMultiWordTests
{
    [Fact]
    public void SearchRanksExactTitlePhraseAboveScatteredTerms()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("phrase", "Avalonia Docs", "https://example.com/"),
            new BookmarkSearchDocument("scattered", "Avalonia guide and API docs", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia docs");

        Assert.Equal("phrase", results[0].Id);
    }

    [Fact]
    public void SearchRanksAllQueryTermsAboveSingleTermMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("all", "Avalonia Documentation", "https://example.com/"),
            new BookmarkSearchDocument("one", "Avalonia", "https://example.com/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia documentation");

        Assert.Equal("all", results[0].Id);
    }

    [Fact]
    public void SearchMatchesOrderedDomainParts()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("learn", "Documentation", "https://learn.microsoft.com/dotnet/"),
            new BookmarkSearchDocument("other", "Learn Something", "https://example.com/microsoft")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("learn microsoft");

        Assert.Equal("learn", results[0].Id);
    }

    [Fact]
    public void SearchStillFindsUsefulResultWhenWordOrderChanges()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("documentation avalonia");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void SearchRanksAdjacentPathTokensAboveSeparatedTokens()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("adjacent", "Docs", "https://example.com/dotnet/csharp/"),
            new BookmarkSearchDocument("separated", "Docs", "https://example.com/dotnet/reference/csharp/")
        ]);

        IReadOnlyList<BookmarkSearchResult> results = index.Search("dotnet csharp");

        Assert.Equal("adjacent", results[0].Id);
    }
}
