using Stranichnik.Search;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class InMemoryBookmarkSearchIndexValidationTests
{
    [Fact]
    public void SearchReturnsEmptyResultsForWhitespaceQuery()
    {
        InMemoryBookmarkSearchIndex index = new();

        IReadOnlyList<BookmarkSearchResult> results = index.Search("   ");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchReturnsEmptyResultsForRuntimeNullQuery()
    {
        InMemoryBookmarkSearchIndex index = new();

        IReadOnlyList<BookmarkSearchResult> results = index.Search(null!);

        Assert.Empty(results);
    }

    [Fact]
    public void SearchReturnsEmptyResultsForEmptyIndex()
    {
        InMemoryBookmarkSearchIndex index = new();

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchReturnsEmptyResultsWhenMaxResultsIsZero()
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchOptions options = new()
        {
            MaxResults = 0
        };

        IReadOnlyList<BookmarkSearchResult> results = index.Search("avalonia", options);

        Assert.Empty(results);
    }

    [Fact]
    public void RebuildRejectsNullDocumentCollection()
    {
        InMemoryBookmarkSearchIndex index = new();

        Assert.Throws<ArgumentNullException>(() => index.Rebuild(null!));
    }

    [Fact]
    public void RebuildKeepsExistingIndexWhenDocumentValidationFails()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("stable", "Stable Document", "https://example.com/stable"));

        Assert.Throws<ArgumentException>(() => index.Rebuild(CreateInvalidRebuildDocuments()));

        BookmarkSearchResult result = Assert.Single(index.Search("stable"));
        Assert.Equal("stable", result.Id);
        Assert.Empty(index.Search("replacement"));
    }

    [Fact]
    public void AddOrUpdateRejectsNullDocument()
    {
        InMemoryBookmarkSearchIndex index = new();

        Assert.Throws<ArgumentNullException>(() => index.AddOrUpdate(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddOrUpdateRejectsEmptyDocumentId(string id)
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchDocument document = new(id, "Title", "https://example.com/");

        Assert.Throws<ArgumentException>(() => index.AddOrUpdate(document));
    }

    [Fact]
    public void AddOrUpdateKeepsExistingIndexWhenDocumentValidationFails()
    {
        InMemoryBookmarkSearchIndex index = new();
        index.AddOrUpdate(new BookmarkSearchDocument("stable", "Stable Document", "https://example.com/stable"));

        Assert.Throws<ArgumentException>(() => index.AddOrUpdate(
            new BookmarkSearchDocument(" ", "Replacement Document", "https://example.com/replacement")));

        BookmarkSearchResult result = Assert.Single(index.Search("stable"));
        Assert.Equal("stable", result.Id);
        Assert.Empty(index.Search("replacement"));
    }

    [Fact]
    public void AddOrUpdateTreatsRuntimeNullTextFieldsAsEmpty()
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchDocument document = new("1", null!, null!, Notes: null);

        index.AddOrUpdate(document);

        Assert.Empty(index.Search("anything"));
    }

    [Fact]
    public void AddOrUpdateIgnoresEmptyRuntimeNullTags()
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchDocument document = new(
            "1",
            "Title",
            "https://example.com/",
            Tags: [null!, " ", "tagged"]);

        index.AddOrUpdate(document);

        BookmarkSearchResult result = Assert.Single(index.Search("tagged"));
        Assert.Equal("1", result.Id);
        Assert.Contains("tags", result.MatchedFields);
    }

    [Fact]
    public void SearchRejectsNegativeMaxResults()
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchOptions options = new()
        {
            MaxResults = -1
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search("avalonia", options));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SearchRejectsInvalidMinimumScore(double minimumScore)
    {
        InMemoryBookmarkSearchIndex index = new();
        BookmarkSearchOptions options = new()
        {
            MinimumScore = minimumScore
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search("avalonia", options));
    }

    [Fact]
    public void RemoveReturnsFalseForUnknownId()
    {
        InMemoryBookmarkSearchIndex index = new();

        bool removed = index.Remove("missing");

        Assert.False(removed);
    }

    [Fact]
    public void ClearIsSafeForEmptyIndex()
    {
        InMemoryBookmarkSearchIndex index = new();

        index.Clear();

        Assert.Empty(index.Search("avalonia"));
    }

    private static IEnumerable<BookmarkSearchDocument> CreateInvalidRebuildDocuments()
    {
        yield return new BookmarkSearchDocument("replacement", "Replacement Document", "https://example.com/replacement");
        yield return new BookmarkSearchDocument(" ", "Invalid Document", "https://example.com/invalid");
    }
}
