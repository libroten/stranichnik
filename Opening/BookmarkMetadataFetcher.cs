using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Icons;

namespace Stranichnik.Opening;

public sealed class BookmarkMetadataFetcher
{
    private const int DefaultMaxHtmlBytes = 512 * 1024;
    private const int DefaultMaxFaviconBytes = 256 * 1024;
    private const int DefaultMaxFaviconCandidates = 4;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly ProductInfoHeaderValue UserAgent = new("Stranichnik", "1.0");
    private readonly HttpClient _httpClient;
    private readonly BookmarkMetadataParser _parser;
    private readonly IIconImageProcessor _iconImageProcessor;
    private readonly int _maxHtmlBytes;
    private readonly int _maxFaviconBytes;
    private readonly int _maxFaviconCandidates;
    private readonly TimeSpan _timeout;

    public BookmarkMetadataFetcher()
        : this(
            new HttpClient(),
            new BookmarkMetadataParser(),
            new IconImageProcessor(),
            DefaultMaxHtmlBytes,
            DefaultMaxFaviconBytes,
            DefaultMaxFaviconCandidates,
            DefaultTimeout)
    {
    }

    public BookmarkMetadataFetcher(
        HttpClient httpClient,
        BookmarkMetadataParser parser,
        int maxHtmlBytes,
        TimeSpan timeout)
        : this(
            httpClient,
            parser,
            new IconImageProcessor(),
            maxHtmlBytes,
            DefaultMaxFaviconBytes,
            DefaultMaxFaviconCandidates,
            timeout)
    {
    }

    public BookmarkMetadataFetcher(
        HttpClient httpClient,
        BookmarkMetadataParser parser,
        IIconImageProcessor iconImageProcessor,
        int maxHtmlBytes,
        int maxFaviconBytes,
        int maxFaviconCandidates,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(iconImageProcessor);

        if (maxHtmlBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHtmlBytes), "Maximum HTML size must be positive.");

        if (maxFaviconBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFaviconBytes), "Maximum favicon size must be positive.");

        if (maxFaviconCandidates <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFaviconCandidates), "Maximum favicon candidate count must be positive.");

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        _httpClient = httpClient;
        _parser = parser;
        _iconImageProcessor = iconImageProcessor;
        _maxHtmlBytes = maxHtmlBytes;
        _maxFaviconBytes = maxFaviconBytes;
        _maxFaviconCandidates = maxFaviconCandidates;
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
                    var iconCandidates = BookmarkMetadataParser.ParseIconCandidatesFromPartialHtml(
                        htmlReadResult.Html,
                        uri);
                    Logs.Print(
                        "Bookmark metadata fallback parsed icon candidates. " +
                        $"IconCandidates={iconCandidates.Count}.");

                    var fallbackResultMetadata = fallbackMetadata with
                    {
                        IconCandidates = iconCandidates
                    };
                    var fallbackFavicon = await TryFetchFaviconAsync(iconCandidates, timeoutCancellation.Token)
                        .ConfigureAwait(false);

                    if (fallbackFavicon is not null)
                        fallbackResultMetadata = fallbackResultMetadata with { Favicon = fallbackFavicon };

                    return BookmarkMetadataFetchResult.Success(fallbackResultMetadata);
                }

                Logs.Print("Bookmark metadata title-only fallback did not find a title.");
                Logs.Print("Bookmark metadata fetch stopped: response is too large.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.ResponseTooLarge);
            }

            var metadata = await Task.Run(
                () => _parser.Parse(htmlReadResult.Html, uri),
                timeoutCancellation.Token).ConfigureAwait(false);
            if (metadata.Title is null)
            {
                Logs.Print("Bookmark metadata parsed: title not found.");
                return BookmarkMetadataFetchResult.Failure(BookmarkMetadataFetchFailureReason.MetadataNotFound);
            }

            Logs.Print($"Bookmark metadata parsed: title found. IconCandidates={metadata.IconCandidates.Count}.");
            var favicon = await TryFetchFaviconAsync(metadata.IconCandidates, timeoutCancellation.Token)
                .ConfigureAwait(false);

            if (favicon is not null)
                metadata = metadata with { Favicon = favicon };

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

    private async Task<BookmarkFetchedIcon?> TryFetchFaviconAsync(
        IReadOnlyList<BookmarkIconCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            Logs.Print("Bookmark favicon fetch skipped: no icon candidates.");
            return null;
        }

        var candidatesToTry = Math.Min(candidates.Count, _maxFaviconCandidates);
        Logs.Print($"Bookmark favicon fetch started. CandidatesToTry={candidatesToTry}.");

        for (var index = 0; index < candidatesToTry; index++)
        {
            var candidate = candidates[index];
            var icon = await TryFetchFaviconCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (icon is not null)
                return icon;
        }

        Logs.Print("Bookmark favicon fetch completed: no usable favicon found.");
        return null;
    }

    private async Task<BookmarkFetchedIcon?> TryFetchFaviconCandidateAsync(
        BookmarkIconCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateFaviconRequest(candidate.Uri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            Logs.Print($"Bookmark favicon HTTP response received. StatusCode={(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                Logs.Print("Bookmark favicon candidate skipped: HTTP response status is not successful.");
                return null;
            }

            var iconReadResult = await ReadBinaryAsync(response.Content, _maxFaviconBytes, cancellationToken)
                .ConfigureAwait(false);
            if (iconReadResult.IsTooLarge)
            {
                Logs.Print("Bookmark favicon candidate skipped: response is too large.");
                return null;
            }

            Logs.Print($"Bookmark favicon candidate downloaded. Bytes={iconReadResult.Bytes.Length}.");
            var processed = await Task.Run(
                () => _iconImageProcessor.Process(iconReadResult.Bytes),
                cancellationToken).ConfigureAwait(false);
            Logs.Print("Bookmark favicon candidate processed successfully.");
            return new BookmarkFetchedIcon(candidate, iconReadResult.Bytes, processed);
        }
        catch (HttpRequestException)
        {
            Logs.Print("Bookmark favicon candidate failed: HTTP request error.");
            return null;
        }
        catch (IOException)
        {
            Logs.Print("Bookmark favicon candidate failed: response read error.");
            return null;
        }
        catch (ArgumentException)
        {
            Logs.Print("Bookmark favicon candidate failed: image data is invalid.");
            return null;
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Bookmark favicon candidate failed: image processing error.");
            return null;
        }
        catch (NotSupportedException)
        {
            Logs.Print("Bookmark favicon candidate failed: image format is not supported.");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logs.Print("Bookmark favicon candidate skipped: request was cancelled or timed out.");
            return null;
        }
    }

    private static HttpRequestMessage CreateFaviconRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/x-icon"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/vnd.microsoft.icon"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
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

    private static async Task<BinaryReadResult> ReadBinaryAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(capacity: Math.Min(maxBytes + 1, 81920));
        var bytes = new byte[8192];
        var totalBytes = 0;

        while (true)
        {
            var readBytes = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (readBytes == 0)
                break;

            var remainingBytes = maxBytes - totalBytes;
            if (readBytes > remainingBytes)
            {
                if (remainingBytes > 0)
                    buffer.Write(bytes, 0, remainingBytes);

                return new BinaryReadResult(buffer.ToArray(), IsTooLarge: true);
            }

            totalBytes += readBytes;
            buffer.Write(bytes, 0, readBytes);
        }

        return new BinaryReadResult(buffer.ToArray(), IsTooLarge: false);
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

    private sealed record BinaryReadResult(byte[] Bytes, bool IsTooLarge);
}
