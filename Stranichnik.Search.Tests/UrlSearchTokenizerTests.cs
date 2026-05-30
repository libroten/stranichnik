using Stranichnik.Search.Internal;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class UrlSearchTokenizerTests
{
    [Fact]
    public void TokenizeExtractsHostDomainAndPathParts()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("https://learn.microsoft.com/dotnet/csharp/");

        Assert.Contains("learn.microsoft.com", tokens.UrlHost.Select(token => token.Value));
        Assert.Contains("learn", tokens.UrlDomainParts.Select(token => token.Value));
        Assert.Contains("microsoft", tokens.UrlDomainParts.Select(token => token.Value));
        Assert.Contains("com", tokens.UrlDomainParts.Select(token => token.Value));
        Assert.Contains("dotnet", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("csharp", tokens.UrlPathParts.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeRemovesLeadingWwwFromHost()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("https://www.example.com/docs");

        Assert.Contains("example.com", tokens.UrlHost.Select(token => token.Value));
        Assert.DoesNotContain("www.example.com", tokens.UrlHost.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeHandlesSchemelessHostAndPath()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("github.com/user/repo");

        Assert.Contains("github.com", tokens.UrlHost.Select(token => token.Value));
        Assert.Contains("github", tokens.UrlDomainParts.Select(token => token.Value));
        Assert.Contains("user", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("repo", tokens.UrlPathParts.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeFallsBackForMalformedUrl()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("not a valid://url example");

        Assert.Contains("not", tokens.UrlText.Select(token => token.Value));
        Assert.Contains("valid", tokens.UrlText.Select(token => token.Value));
        Assert.Contains("url", tokens.UrlDomainParts.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeDecodesPercentEncodedPathParts()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("https://example.com/%D0%BF%D0%BE%D0%B8%D1%81%D0%BA");

        Assert.Contains("поиск", tokens.UrlPathParts.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeExtractsQueryStringTokens()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("https://example.com/search?q=avalonia&lang=ru");

        Assert.Contains("search", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("q", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("avalonia", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("lang", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("ru", tokens.UrlPathParts.Select(token => token.Value));
    }

    [Fact]
    public void TokenizeHandlesCyrillicPathTokens()
    {
        UrlSearchTokens tokens = UrlSearchTokenizer.Tokenize("https://example.com/документы/поиск");

        Assert.Contains("документы", tokens.UrlPathParts.Select(token => token.Value));
        Assert.Contains("поиск", tokens.UrlPathParts.Select(token => token.Value));
    }
}
