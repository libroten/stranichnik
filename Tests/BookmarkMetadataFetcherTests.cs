using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Icons;
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
            actualUri ??= request.RequestUri;
            return CreateHtmlResponse("<title>Title</title>");
        });
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedUri, actualUri?.AbsoluteUri);
    }

    [Fact]
    public async Task FetchAsync_returns_icon_candidates_resolved_against_request_uri()
    {
        using var httpClient = CreateSyncClient((_, _) => CreateHtmlResponse("""
            <html>
              <head>
                <title>Title</title>
                <link rel="icon" href="/custom-icon.png">
              </head>
            </html>
            """));
        var fetcher = CreateFetcher(httpClient);

        var result = await fetcher.FetchAsync("example.com/articles/page");

        Assert.True(result.IsSuccess);
        Assert.Collection(
            Assert.IsType<BookmarkPageMetadata>(result.Metadata).IconCandidates,
            candidate => Assert.Equal(new Uri("https://example.com/custom-icon.png"), candidate.Uri),
            candidate => Assert.Equal(new Uri("https://example.com/favicon.ico"), candidate.Uri));
    }

    [Fact]
    public async Task FetchAsync_downloads_and_processes_first_usable_favicon()
    {
        var processor = new FakeIconImageProcessor();
        using var httpClient = CreateSyncClient((request, _) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/articles/page" => CreateHtmlResponse("""
                    <html>
                      <head>
                        <title>Title</title>
                        <link rel="icon" href="/custom-icon.png">
                      </head>
                    </html>
                    """),
                "/custom-icon.png" => CreateBinaryResponse(CustomFaviconBytes, "image/png"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var fetcher = CreateFetcher(httpClient, iconImageProcessor: processor);

        var result = await fetcher.FetchAsync("https://example.com/articles/page");

        Assert.True(result.IsSuccess);
        var favicon = Assert.IsType<BookmarkFetchedIcon>(result.Metadata?.Favicon);
        Assert.Equal(new Uri("https://example.com/custom-icon.png"), favicon.Candidate.Uri);
        Assert.Equal(CustomFaviconBytes, favicon.OriginalBytes.ToArray());
        Assert.Equal(ProcessedFaviconBytes, favicon.Image.Bytes.ToArray());
        Assert.Equal(CustomFaviconBytes, processor.LastInputBytes);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task FetchAsync_tries_fallback_favicon_when_declared_candidate_fails()
    {
        var processor = new FakeIconImageProcessor();
        using var httpClient = CreateSyncClient((request, _) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/" => CreateHtmlResponse("""
                    <html>
                      <head>
                        <title>Title</title>
                        <link rel="icon" href="/missing.png">
                      </head>
                    </html>
                    """),
                "/missing.png" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/favicon.ico" => CreateBinaryResponse(FallbackFaviconBytes, "image/x-icon"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var fetcher = CreateFetcher(httpClient, iconImageProcessor: processor);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        var favicon = Assert.IsType<BookmarkFetchedIcon>(result.Metadata?.Favicon);
        Assert.Equal(new Uri("https://example.com/favicon.ico"), favicon.Candidate.Uri);
        Assert.Equal(FallbackFaviconBytes, favicon.OriginalBytes.ToArray());
        Assert.Equal(FallbackFaviconBytes, processor.LastInputBytes);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task FetchAsync_skips_oversized_favicon_candidate()
    {
        var processor = new FakeIconImageProcessor();
        using var httpClient = CreateSyncClient((request, _) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/" => CreateHtmlResponse("""
                    <html>
                      <head>
                        <title>Title</title>
                        <link rel="icon" href="/large.png">
                      </head>
                    </html>
                    """),
                "/large.png" => CreateBinaryResponse(LargeFaviconBytes, "image/png"),
                "/favicon.ico" => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var fetcher = CreateFetcher(
            httpClient,
            iconImageProcessor: processor,
            maxFaviconBytes: 2);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Metadata?.Favicon);
        Assert.Equal(0, processor.CallCount);
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
    public async Task FetchAsync_keeps_title_when_favicon_fetch_exceeds_timeout()
    {
        using var httpClient = CreateClient(async (request, cancellationToken) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/" => CreateHtmlResponse("""
                    <html>
                      <head>
                        <title>Title before favicon timeout</title>
                        <link rel="icon" href="/slow-icon.png">
                      </head>
                    </html>
                    """),
                "/slow-icon.png" => await CreateSlowFaviconResponseAsync(cancellationToken),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var fetcher = CreateFetcher(httpClient, timeout: TimeSpan.FromMilliseconds(20));

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("Title before favicon timeout", result.Metadata?.Title);
        Assert.Null(result.Metadata?.Favicon);
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
    public async Task FetchAsync_downloads_favicon_from_large_html_fallback_candidates()
    {
        var processor = new FakeIconImageProcessor();
        var html = """
            <html>
              <head>
                <title>Large title</title>
                <link rel="icon" href="/early-icon.png">
              </head>
              <body>
            """ + new string('x', 200);
        using var httpClient = CreateSyncClient((request, _) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/" => CreateHtmlResponse(html),
                "/early-icon.png" => CreateBinaryResponse(CustomFaviconBytes, "image/png"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var fetcher = CreateFetcher(
            httpClient,
            maxHtmlBytes: 160,
            iconImageProcessor: processor);

        var result = await fetcher.FetchAsync("https://example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("Large title", result.Metadata?.Title);
        var favicon = Assert.IsType<BookmarkFetchedIcon>(result.Metadata?.Favicon);
        Assert.Equal(new Uri("https://example.com/early-icon.png"), favicon.Candidate.Uri);
        Assert.Equal(CustomFaviconBytes, favicon.OriginalBytes.ToArray());
        Assert.Equal(1, processor.CallCount);
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
        int maxFaviconBytes = 256 * 1024,
        IIconImageProcessor? iconImageProcessor = null,
        TimeSpan? timeout = null)
    {
        return new BookmarkMetadataFetcher(
            httpClient,
            new BookmarkMetadataParser(),
            iconImageProcessor ?? new FakeIconImageProcessor(),
            maxHtmlBytes,
            maxFaviconBytes,
            maxFaviconCandidates: 4,
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

    private static HttpResponseMessage CreateBinaryResponse(byte[] bytes, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new(mediaType);
        return response;
    }

    private static async Task<HttpResponseMessage> CreateSlowFaviconResponseAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        return CreateBinaryResponse(CustomFaviconBytes, "image/png");
    }

    private sealed class FakeIconImageProcessor : IIconImageProcessor
    {
        public int CallCount { get; private set; }

        public byte[] LastInputBytes { get; private set; } = [];

        public ProcessedIconImage Process(ReadOnlyMemory<byte> originalBytes)
        {
            CallCount++;
            LastInputBytes = originalBytes.ToArray();

            return new(
                "image/png",
                Width: 64,
                Height: 64,
                ProcessedFaviconBytes);
        }
    }

    private static readonly byte[] CustomFaviconBytes = [1, 2, 3];
    private static readonly byte[] FallbackFaviconBytes = [4, 5, 6];
    private static readonly byte[] LargeFaviconBytes = [1, 2, 3, 4];
    private static readonly byte[] ProcessedFaviconBytes = [9, 8, 7];

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
