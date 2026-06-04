using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;

namespace Stranichnik.Opening;

public sealed class BookmarkMetadataFetcher
{
    private const int DefaultMaxHtmlBytes = 512 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly ProductInfoHeaderValue UserAgent = new("Stranichnik", "1.0");
    private readonly HttpClient _httpClient;
    private readonly BookmarkMetadataParser _parser;
    private readonly int _maxHtmlBytes;
    private readonly TimeSpan _timeout;

    public BookmarkMetadataFetcher()
        : this(new HttpClient(), new BookmarkMetadataParser(), DefaultMaxHtmlBytes, DefaultTimeout)
    {
    }

    public BookmarkMetadataFetcher(
        HttpClient httpClient,
        BookmarkMetadataParser parser,
        int maxHtmlBytes,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(parser);

        if (maxHtmlBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHtmlBytes), "Maximum HTML size must be positive.");

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        _httpClient = httpClient;
        _parser = parser;
        _maxHtmlBytes = maxHtmlBytes;
        _timeout = timeout;
    }

    public async Task<BookmarkMetadataFetchResult> FetchAsync(
        string addressText,
        CancellationToken cancellationToken = default)
    {
        var urlStatus = BookmarkUrlNormalizer.TryNormalizeForOpening(addressText, out var uri);
        if (urlStatus == BookmarkUrlOpenStatus.InvalidAddress)
        {
            Logs.Print("Bookmark metadata fetch skipped: invalid address.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.InvalidAddress);
        }

        if (urlStatus == BookmarkUrlOpenStatus.UnsupportedScheme)
        {
            Logs.Print("Bookmark metadata fetch skipped: unsupported URL scheme.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.UnsupportedScheme);
        }

        ArgumentNullException.ThrowIfNull(uri);

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(_timeout);

        try
        {
            Logs.Print("Bookmark metadata fetch started.");
            using var request = CreateRequest(uri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCancellation.Token)
                .ConfigureAwait(false);

            Logs.Print($"Bookmark metadata HTTP response received. StatusCode={(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                Logs.Print("Bookmark metadata fetch failed: HTTP response status is not successful.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.NetworkFailure);
            }

            if (!IsHtmlResponse(response.Content.Headers.ContentType))
            {
                Logs.Print("Bookmark metadata fetch stopped: response is not HTML.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.NonHtmlResponse);
            }

            var htmlReadResult = await ReadHtmlAsync(response.Content, timeoutCancellation.Token).ConfigureAwait(false);
            if (htmlReadResult.IsTooLarge)
            {
                Logs.Print("Bookmark metadata response is too large; trying title-only fallback.");
                var fallbackMetadata = BookmarkMetadataParser.ParseTitleTagOnly(htmlReadResult.Html);
                if (fallbackMetadata.Title is not null)
                {
                    Logs.Print("Bookmark metadata title-only fallback succeeded.");
                    return BookmarkMetadataFetchResult.Success(fallbackMetadata);
                }

                Logs.Print("Bookmark metadata title-only fallback did not find a title.");
                Logs.Print("Bookmark metadata fetch stopped: response is too large.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.ResponseTooLarge);
            }

            var metadata = await Task.Run(() => _parser.Parse(htmlReadResult.Html), timeoutCancellation.Token).ConfigureAwait(false);
            if (metadata.Title is null)
            {
                Logs.Print("Bookmark metadata parsed: title not found.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.MetadataNotFound);
            }

            Logs.Print("Bookmark metadata parsed: title found.");
            return BookmarkMetadataFetchResult.Success(metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logs.Print("Bookmark metadata fetch cancelled.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.Cancelled);
        }
        catch (OperationCanceledException)
        {
            Logs.Print("Bookmark metadata fetch timed out.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.Timeout);
        }
        catch (HttpRequestException)
        {
            Logs.Print("Bookmark metadata fetch failed: HTTP request error.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.NetworkFailure);
        }
        catch (IOException)
        {
            Logs.Print("Bookmark metadata fetch failed: response read error.");
            return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.NetworkFailure);
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        return request;
    }

    private static bool IsHtmlResponse(MediaTypeHeaderValue? contentType)
    {
        if (contentType is null)
            return true;

        return string.Equals(contentType.MediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(contentType.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HtmlReadResult> ReadHtmlAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(capacity: Math.Min(_maxHtmlBytes + 1, 81920));
        var bytes = new byte[8192];
        var totalBytes = 0;

        while (true)
        {
            var readBytes = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (readBytes == 0)
                break;

            var remainingBytes = _maxHtmlBytes - totalBytes;
            if (readBytes > remainingBytes)
            {
                if (remainingBytes > 0)
                    buffer.Write(bytes, 0, remainingBytes);

                return new HtmlReadResult(
                    DecodeContent(buffer.ToArray(), content.Headers.ContentType),
                    IsTooLarge: true);
            }

            totalBytes += readBytes;
            buffer.Write(bytes, 0, readBytes);
        }

        return new HtmlReadResult(
            DecodeContent(buffer.ToArray(), content.Headers.ContentType),
            IsTooLarge: false);
    }

    private static string DecodeContent(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8.GetString(bytes);

        try
        {
            return Encoding.GetEncoding(charset).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (NotSupportedException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private sealed record HtmlReadResult(string Html, bool IsTooLarge);
}
