using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class SearchDiagnosticsTests
{
    [Fact]
    public void SearchDoesNotReturnDiagnosticsByDefault()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        BookmarkSearchResult result = Assert.Single(index.Search("avalonia"));

        Assert.Null(result.Diagnostics);
    }

    [Fact]
    public void SearchReturnsDiagnosticsWhenEnabled()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        BookmarkSearchOptions options = new()
        {
            IncludeDiagnostics = true
        };

        BookmarkSearchResult result = Assert.Single(index.Search("avalonia", options));

        Assert.NotNull(result.Diagnostics);
        Assert.Contains("exact title", result.Diagnostics!, StringComparison.Ordinal);
        Assert.Contains("coverage: 1/1", result.Diagnostics!, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiagnosticsMentionPrefixMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        BookmarkSearchOptions options = new()
        {
            IncludeDiagnostics = true
        };

        BookmarkSearchResult result = Assert.Single(index.Search("aval", options));

        Assert.Contains("prefix title", result.Diagnostics!, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiagnosticsMentionFuzzyMatch()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));
        BookmarkSearchOptions options = new()
        {
            IncludeDiagnostics = true
        };

        BookmarkSearchResult result = Assert.Single(index.Search("avlaonia", options));

        Assert.Contains("fuzzy2 title", result.Diagnostics!, StringComparison.Ordinal);
    }

    [Fact]
    public void EnablingDiagnosticsDoesNotChangeOrdering()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.Rebuild(
        [
            new BookmarkSearchDocument("a", "Avalonia Documentation", "https://example.com/"),
            new BookmarkSearchDocument("b", "Avalonia Guide", "https://example.com/")
        ]);
        IReadOnlyList<BookmarkSearchResult> withoutDiagnostics = index.Search("avalonia");
        IReadOnlyList<BookmarkSearchResult> withDiagnostics = index.Search(
            "avalonia",
            new BookmarkSearchOptions { IncludeDiagnostics = true });

        Assert.Collection(
            withDiagnostics,
            result => Assert.Equal(withoutDiagnostics[0].Id, result.Id),
            result => Assert.Equal(withoutDiagnostics[1].Id, result.Id));
    }
}
