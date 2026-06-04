using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Opening;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkMetadataFetcherTests
{
    [Fact]
    public async Task FetchAsync_returns_title_from_successful_html_response()
    {
        using var httpClient = CreateSyncClient((request, _) => CreateHtmlResponse("""
            <html>
              <head>
                <meta property="og:title" content="Fetched title">
              </head>
            </html>
            """));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("Fetched title", result.Metadata?.Title);
    }

    [Theory]
    [InlineData("habr.com", "https://habr.com/")]
    [InlineData("pikabu.ru/feed", "https://pikabu.ru/feed")]
    [InlineData("https://example.com/path", "https://example.com/path")]
    [InlineData("localhost:5000/page", "https://localhost:5000/page")]
    [InlineData("127.0.0.1:8080/page", "https://127.0.0.1:8080/page")]
    public async Task FetchAsync_uses_normalized_request_uri(string input, string expectedUri)
    {
        Uri? actualUri = null;
        using var httpClient = CreateSyncClient((request, _) =>
        {
            actualUri = request.RequestUri;
            return CreateHtmlResponse("<title>Title</title>");
        });
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedUri, actualUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("hello world")]
    [InlineData("example.com:abc")]
    public async Task FetchAsync_rejects_invalid_address_without_http_request(string input)
    {
        using var httpClient = CreateSyncClient((_, _) => throw new InvalidOperationException("Request should not be sent."));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.InvalidAddress, result.FailureReason);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:test@example.com")]
    public async Task FetchAsync_rejects_unsupported_scheme_without_http_request(string input)
    {
        using var httpClient = CreateSyncClient((_, _) => throw new InvalidOperationException("Request should not be sent."));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.UnsupportedScheme, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_rejects_non_html_response()
    {
        using var httpClient = CreateSyncClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"title\":\"Nope\"}")
            };
            response.Content.Headers.ContentType = new("application/json");
            return response;
        });
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.NonHtmlResponse, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_metadata_not_found_when_html_has_no_title()
    {
        using var httpClient = CreateSyncClient((_, _) => CreateHtmlResponse("<html><body>No metadata</body></html>"));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.MetadataNotFound, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_falls_back_to_utf8_when_response_charset_is_not_supported()
    {
        using var httpClient = CreateSyncClient((_, _) =>
        {
            var response = CreateHtmlResponse("<title>UTF-8 title</title>");
            response.Content.Headers.ContentType!.CharSet = "windows-1251";
            return response;
        });
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("UTF-8 title", result.Metadata?.Title);
    }

    [Fact]
    public async Task FetchAsync_returns_network_failure_for_http_request_error()
    {
        using var httpClient = CreateSyncClient((_, _) => throw new HttpRequestException("Network failed."));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.NetworkFailure, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_network_failure_for_unsuccessful_status_code()
    {
        using var httpClient = CreateSyncClient((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<title>Not found</title>")
        });
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.NetworkFailure, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_cancelled_when_token_is_cancelled()
    {
        using var httpClient = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return CreateHtmlResponse("<title>Too late</title>");
        });
        var fetcher = CreateFetcher(httpClient, timeout: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await fetcher.FetchAsync("https://example.com", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.Cancelled, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_timeout_when_request_exceeds_timeout()
    {
        using var httpClient = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return CreateHtmlResponse("<title>Too late</title>");
        });
        var fetcher = CreateFetcher(httpClient, timeout: TimeSpan.FromMilliseconds(1));

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.Timeout, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_response_too_large_when_html_exceeds_limit()
    {
        using var httpClient = CreateSyncClient((_, _) => CreateHtmlResponse("<title>Large</title>"));
        var fetcher = CreateFetcher(httpClient, maxHtmlBytes: 4);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.ResponseTooLarge, result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_returns_title_only_fallback_when_large_html_contains_complete_title_near_start()
    {
        var html = "<html><head><title>Large &amp; useful title</title></head><body>" +
            new string('x', 200);
        using var httpClient = CreateSyncClient((_, _) => CreateHtmlResponse(html));
        var fetcher = CreateFetcher(httpClient, maxHtmlBytes: 80);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("Large & useful title", result.Metadata?.Title);
    }

    [Fact]
    public async Task FetchAsync_returns_response_too_large_when_title_only_fallback_cannot_find_complete_title()
    {
        var html = "<html><head><title>Large title without closing tag in downloaded prefix</title></head><body>" +
            new string('x', 200);
        using var httpClient = CreateSyncClient((_, _) => CreateHtmlResponse(html));
        var fetcher = CreateFetcher(httpClient, maxHtmlBytes: 32);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(BookmarkMetadataFetchFailureReason.ResponseTooLarge, result.FailureReason);
    }

    private static BookmarkMetadataFetcher CreateFetcher(
        HttpClient httpClient,
        int maxHtmlBytes = 512 * 1024,
        TimeSpan? timeout = null)
    {
        return new BookmarkMetadataFetcher(
            httpClient,
            new BookmarkMetadataParser(),
            maxHtmlBytes,
            timeout ?? TimeSpan.FromSeconds(5));
    }

    private static HttpClient CreateSyncClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler((request, cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken))));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler));
    }

    private static HttpResponseMessage CreateHtmlResponse(string html)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        };
        response.Content.Headers.ContentType = new("text/html");
        return response;
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
