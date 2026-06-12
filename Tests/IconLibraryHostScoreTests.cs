using Stranichnik.Icons;
using Xunit;

namespace Stranichnik.Tests;

public sealed class IconLibraryHostScoreTests
{
    [Theory]
    [InlineData("docs.google.com", "mail.google.com", 2)]
    [InlineData("news.ycombinator.com", "ycombinator.com", 2)]
    [InlineData("example.com", "other.com", 1)]
    [InlineData("example.com:443", "www.example.com", 2)]
    [InlineData("LOCALHOST", "localhost", 1)]
    [InlineData("127.0.0.1:5000", "127.0.0.1", 1)]
    [InlineData("127.0.0.1", "127.0.0.2", 0)]
    [InlineData("", "example.com", 0)]
    [InlineData("not a url", "example.com", 0)]
    [InlineData("example.com", "not a url", 0)]
    public void Compute_scores_host_matches(string targetUrl, string usedUrl, int expectedScore)
    {
        Assert.Equal(expectedScore, IconLibraryHostScore.Compute(targetUrl, usedUrl));
    }
}
