using Stranichnik.Opening;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkUrlNormalizerTests
{
    [Theory]
    [InlineData("https://habr.com", "https://habr.com/")]
    [InlineData("http://pikabu.ru/feed", "http://pikabu.ru/feed")]
    [InlineData("http://localhost:5000/page", "http://localhost:5000/page")]
    [InlineData("http://127.0.0.1:8080/page", "http://127.0.0.1:8080/page")]
    [InlineData("  https://example.com/path?q=1#part  ", "https://example.com/path?q=1#part")]
    public void TryNormalizeForOpening_keeps_supported_absolute_urls(
        string input,
        string expected)
    {
        var status = BookmarkUrlNormalizer.TryNormalizeForOpening(input, out var uri);

        Assert.Equal(BookmarkUrlOpenStatus.Success, status);
        Assert.NotNull(uri);
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("habr.com", "https://habr.com/")]
    [InlineData("pikabu.ru/feed", "https://pikabu.ru/feed")]
    [InlineData("www.example.com?q=1", "https://www.example.com/?q=1")]
    [InlineData("example.co.uk/path#fragment", "https://example.co.uk/path#fragment")]
    [InlineData("example.com:8443/admin", "https://example.com:8443/admin")]
    [InlineData("localhost", "https://localhost/")]
    [InlineData("localhost:5000", "https://localhost:5000/")]
    [InlineData("localhost:5000/page", "https://localhost:5000/page")]
    [InlineData("127.0.0.1:8080/page", "https://127.0.0.1:8080/page")]
    [InlineData("192.168.1.10", "https://192.168.1.10/")]
    public void TryNormalizeForOpening_adds_https_to_web_addresses_without_scheme(
        string input,
        string expected)
    {
        var status = BookmarkUrlNormalizer.TryNormalizeForOpening(input, out var uri);

        Assert.Equal(BookmarkUrlOpenStatus.Success, status);
        Assert.NotNull(uri);
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:test@example.com")]
    [InlineData("file:///tmp/example.txt")]
    [InlineData("custom+scheme:value")]
    public void TryNormalizeForOpening_rejects_explicit_unsupported_schemes(string input)
    {
        var status = BookmarkUrlNormalizer.TryNormalizeForOpening(input, out var uri);

        Assert.Equal(BookmarkUrlOpenStatus.UnsupportedScheme, status);
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("hello world")]
    [InlineData("not/a/domain")]
    [InlineData("example")]
    [InlineData(".example.com")]
    [InlineData("example.")]
    [InlineData("example..com")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("example.c")]
    [InlineData("example.com:")]
    [InlineData("example.com:abc")]
    [InlineData("example.com:70000")]
    [InlineData("localhost:")]
    [InlineData("localhost:abc")]
    [InlineData("127.0.0.1:70000")]
    [InlineData("256.1.1.1")]
    [InlineData("example.com\\path")]
    [InlineData("example.com/<script>")]
    public void TryNormalizeForOpening_rejects_invalid_addresses(string input)
    {
        var status = BookmarkUrlNormalizer.TryNormalizeForOpening(input, out var uri);

        Assert.Equal(BookmarkUrlOpenStatus.InvalidAddress, status);
        Assert.Null(uri);
    }
}
