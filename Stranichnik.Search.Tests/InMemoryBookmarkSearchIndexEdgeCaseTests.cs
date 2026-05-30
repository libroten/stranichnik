using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexEdgeCaseTests
{
    [Fact]
    public void SearchHandlesVeryLongTitle()
    {
        InMemoryBookmarkSearchIndex index = new();
        string longTitle = string.Join(' ', Enumerable.Repeat("avalonia", 1_000));
        index.AddOrUpdate(new BookmarkSearchDocument("1", longTitle, "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void SearchHandlesVeryLongUrl()
    {
        InMemoryBookmarkSearchIndex index = new();
        string longUrl = "https://example.com/" + string.Join('/', Enumerable.Repeat("avalonia", 1_000));
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Long URL", longUrl));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void SearchHandlesEmptyTitleAndUrl()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", string.Empty, string.Empty));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("anything");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchHandlesUnusualUnicodeInput()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Документация ＡＢＣ", "https://example.com/"));

        IReadOnlyList<BookmarkSearchResult> results = index.Search("abc");

        BookmarkSearchResult result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public void SearchOverClearedIndexReturnsEmptyResults()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Avalonia Documentation", "https://example.com/"));

        index.Clear();

        Assert.Empty(index.Search("avalonia"));
    }

    [Fact]
    public void RepeatedAddUpdateRemoveForSameIdIsStable()
    {
        InMemoryBookmarkSearchIndex index = new();

        index.AddOrUpdate(new BookmarkSearchDocument("1", "Old Title", "https://example.com/old"));
        index.AddOrUpdate(new BookmarkSearchDocument("1", "New Title", "https://example.com/new"));
        bool removed = index.Remove("1");
        index.AddOrUpdate(new BookmarkSearchDocument("1", "Final Title", "https://example.com/final"));

        Assert.True(removed);
        Assert.Empty(index.Search("old"));
        Assert.Empty(index.Search("new"));
        BookmarkSearchResult result = Assert.Single(index.Search("final"));
        Assert.Equal("1", result.Id);
    }
}
