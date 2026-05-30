using Stranichnik.Search.Internal;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class SearchTokenizerTests
{
    [Fact]
    public void TokenizeSplitsTextOnPunctuation()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("Avalonia: docs, search.");

        Assert.Collection(
            tokens,
            token => Assert.Equal("avalonia", token.Value),
            token => Assert.Equal("docs", token.Value),
            token => Assert.Equal("search", token.Value));
    }

    [Fact]
    public void TokenizeHandlesMixedRussianAndEnglishText()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("Avalonia документация поиск");

        Assert.Collection(
            tokens,
            token => Assert.Equal("avalonia", token.Value),
            token => Assert.Equal("документация", token.Value),
            token => Assert.Equal("поиск", token.Value));
    }

    [Fact]
    public void TokenizeKeepsOneLetterTokens()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("R language");

        Assert.Collection(
            tokens,
            token => Assert.Equal("r", token.Value),
            token => Assert.Equal("language", token.Value));
    }

    [Fact]
    public void TokenizeEmitsCSharpAlias()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("C# guide");

        Assert.Collection(
            tokens,
            token => Assert.Equal(("csharp", 0), (token.Value, token.Position)),
            token => Assert.Equal(("c", 0), (token.Value, token.Position)),
            token => Assert.Equal(("guide", 1), (token.Value, token.Position)));
    }

    [Fact]
    public void TokenizeEmitsFSharpAlias()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("F# guide");

        Assert.Collection(
            tokens,
            token => Assert.Equal(("fsharp", 0), (token.Value, token.Position)),
            token => Assert.Equal(("f", 0), (token.Value, token.Position)),
            token => Assert.Equal(("guide", 1), (token.Value, token.Position)));
    }

    [Fact]
    public void TokenizeEmitsDotNetAlias()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize(".NET documentation");

        Assert.Collection(
            tokens,
            token => Assert.Equal(("dotnet", 0), (token.Value, token.Position)),
            token => Assert.Equal(("net", 0), (token.Value, token.Position)),
            token => Assert.Equal(("documentation", 1), (token.Value, token.Position)));
    }

    [Fact]
    public void TokenizePreservesTokenPositions()
    {
        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize("one two three");

        Assert.Collection(
            tokens,
            token => Assert.Equal(0, token.Position),
            token => Assert.Equal(1, token.Position),
            token => Assert.Equal(2, token.Position));
    }

    [Fact]
    public void TokenizeTruncatesVeryLongTokens()
    {
        string longToken = new('a', SearchTokenizer.MaximumTokenLength + 10);

        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize(longToken);

        SearchToken token = Assert.Single(tokens);
        Assert.Equal(SearchTokenizer.MaximumTokenLength, token.Value.Length);
    }

    [Fact]
    public void TokenizeCapsTokenCountPerField()
    {
        string text = string.Join(' ', Enumerable.Repeat("token", SearchTokenizer.MaximumTokensPerField + 10));

        IReadOnlyList<SearchToken> tokens = SearchTokenizer.Tokenize(text);

        Assert.Equal(SearchTokenizer.MaximumTokensPerField, tokens.Count);
    }
}
