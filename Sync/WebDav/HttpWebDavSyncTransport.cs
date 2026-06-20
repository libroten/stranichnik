using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Stranichnik.Diagnostics;
using Stranichnik.Sync;

namespace Stranichnik.Sync.WebDav;

public sealed class HttpWebDavSyncTransport : IWebDavSyncTransport, IWebDavTempObjectCleaner
{
    private static readonly TimeSpan TempObjectRetentionPeriod = TimeSpan.FromDays(1);
    private static readonly HttpMethod MkColMethod = new("MKCOL");
    private static readonly HttpMethod MoveMethod = new("MOVE");
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly XNamespace DavNamespace = "DAV:";

    private readonly HttpClient _httpClient;
    private readonly Uri _repositoryUri;
    private readonly Action<string> _log;

    public HttpWebDavSyncTransport(
        HttpClient httpClient,
        Uri repositoryUri,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(repositoryUri);

        _httpClient = httpClient;
        _repositoryUri = EnsureTrailingSlash(repositoryUri);
        _log = log ?? Logs.Print;
    }

    public async Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        _log($"WebDAV repository ensure started. Directories={SyncRemoteRepositoryLayout.RequiredDirectories.Length}.");
        foreach (var directory in SyncRemoteRepositoryLayout.RequiredDirectories)
            await EnsureDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        _log("WebDAV repository ensure finished.");
    }

    public async Task CleanupStaleTempObjectsAsync(CancellationToken cancellationToken)
    {
        _log("WebDAV stale temp cleanup started.");
        var tempObjects = await ListAsync(SyncRemoteRepositoryLayout.TempDirectory, cancellationToken)
            .ConfigureAwait(false);
        var cutoffUtc = DateTimeOffset.UtcNow.Subtract(TempObjectRetentionPeriod);
        var deletedCount = 0;
        var skippedCount = 0;

        foreach (var tempObject in tempObjects)
        {
            if (tempObject.LastModifiedUtc is null ||
                tempObject.LastModifiedUtc.Value > cutoffUtc)
            {
                skippedCount++;
                continue;
            }

            try
            {
                var result = await DeleteAsync(
                        tempObject.RelativePath,
                        tempObject.ETag,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.Status == SyncDeleteStatus.DeletedOrMissing)
                    deletedCount++;
            }
            catch (HttpRequestException)
            {
                _log("WebDAV stale temp cleanup item failed: HTTP request error.");
            }
            catch (TaskCanceledException)
            {
                _log("WebDAV stale temp cleanup item failed: operation canceled.");
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }

        _log(
            "WebDAV stale temp cleanup finished. " +
            $"Deleted={deletedCount}; " +
            $"Skipped={skippedCount}; " +
            $"Total={tempObjects.Count}.");
    }

    public async Task<IReadOnlyList<SyncRemoteObjectInfo>> ListAsync(
        string relativeDirectory,
        CancellationToken cancellationToken)
    {
        _log("WebDAV list request started.");
        using var request = new HttpRequestMessage(
            PropFindMethod,
            CreateRemoteUri(relativeDirectory));
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = new StringContent(CreatePropFindBody(), Encoding.UTF8, "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _log($"WebDAV list response received. StatusCode={(int)response.StatusCode}.");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log("WebDAV list finished: remote directory missing.");
            return [];
        }

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var objects = ParsePropFindResponse(xml, relativeDirectory);
        _log($"WebDAV list finished. Objects={objects.Count}; ResponseChars={xml.Length}.");
        return objects;
    }

    public async Task<byte[]?> GetAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        _log("WebDAV get request started.");
        using var response = await _httpClient.GetAsync(
            CreateRemoteUri(relativePath),
            cancellationToken).ConfigureAwait(false);

        _log($"WebDAV get response received. StatusCode={(int)response.StatusCode}.");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log("WebDAV get finished: object missing.");
            return null;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        _log($"WebDAV get finished. Bytes={bytes.Length}.");
        return bytes;
    }

    public async Task<SyncPutResult> PutAsync(
        string relativePath,
        byte[] bytes,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _log(
            "WebDAV put requested. " +
            $"Mode={(createOnly ? "CreateOnly" : "Update")}; " +
            $"ExpectedEtagPresent={!string.IsNullOrWhiteSpace(expectedEtag)}; " +
            $"Bytes={bytes.Length}.");

        if (createOnly && string.IsNullOrWhiteSpace(expectedEtag))
        {
            return await PutNewObjectAtomicallyAsync(
                    relativePath,
                    bytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await PutFinalObjectAsync(
                relativePath,
                bytes,
                expectedEtag,
                createOnly,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SyncDeleteResult> DeleteAsync(
        string relativePath,
        string? expectedEtag,
        CancellationToken cancellationToken)
    {
        _log(
            "WebDAV delete request started. " +
            $"ExpectedEtagPresent={!string.IsNullOrWhiteSpace(expectedEtag)}.");
        using var request = new HttpRequestMessage(HttpMethod.Delete, CreateRemoteUri(relativePath));
        if (!string.IsNullOrWhiteSpace(expectedEtag))
            request.Headers.TryAddWithoutValidation("If-Match", expectedEtag);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var etag = response.Headers.ETag?.ToString();
        _log($"WebDAV delete response received. StatusCode={(int)response.StatusCode}.");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log("WebDAV delete finished: object already missing.");
            return SyncDeleteResult.DeletedOrMissing();
        }

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            _log($"WebDAV delete finished: precondition failed. RemoteEtagPresent={etag is not null}.");
            return SyncDeleteResult.PreconditionFailed(etag);
        }

        response.EnsureSuccessStatusCode();
        _log("WebDAV delete finished: deleted.");
        return SyncDeleteResult.DeletedOrMissing();
    }

    private async Task<SyncPutResult> PutNewObjectAtomicallyAsync(
        string relativePath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        _log("WebDAV atomic create started: temp upload.");
        var tempPath = CreateTempUploadPath(relativePath);
        var tempPutResult = await PutFinalObjectAsync(
                tempPath,
                bytes,
                expectedEtag: null,
                createOnly: true,
                cancellationToken)
            .ConfigureAwait(false);

        if (tempPutResult.Status != SyncPutStatus.CreatedOrUpdated)
        {
            _log($"WebDAV atomic create stopped: temp upload status={tempPutResult.Status}.");
            return tempPutResult;
        }

        _log("WebDAV atomic create temp upload succeeded; moving to final object.");
        var moveResult = await MoveTempObjectToFinalPathAsync(
                tempPath,
                relativePath,
                cancellationToken)
            .ConfigureAwait(false);

        if (moveResult.IsMoveUnsupported)
        {
            _log("WebDAV atomic create MOVE unsupported; falling back to direct create-only PUT.");
            var fallbackResult = await PutFinalObjectAsync(
                    relativePath,
                    bytes,
                    expectedEtag: null,
                    createOnly: true,
                    cancellationToken)
                .ConfigureAwait(false);

            await TryDeleteTempObjectAsync(tempPath, cancellationToken).ConfigureAwait(false);
            _log($"WebDAV atomic create fallback finished. Status={fallbackResult.Status}.");
            return fallbackResult;
        }

        if (moveResult.Result.Status == SyncPutStatus.CreatedOrUpdated)
        {
            _log("WebDAV atomic create finished through MOVE.");
            return moveResult.Result;
        }

        _log($"WebDAV atomic create MOVE did not complete. Status={moveResult.Result.Status}; cleaning temp object.");
        await TryDeleteTempObjectAsync(tempPath, cancellationToken).ConfigureAwait(false);
        return moveResult.Result;
    }

    private async Task<SyncPutResult> PutFinalObjectAsync(
        string relativePath,
        byte[] bytes,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        _log(
            "WebDAV final PUT started. " +
            $"CreateOnly={createOnly}; " +
            $"ExpectedEtagPresent={!string.IsNullOrWhiteSpace(expectedEtag)}; " +
            $"Bytes={bytes.Length}.");
        using var request = new HttpRequestMessage(HttpMethod.Put, CreateRemoteUri(relativePath));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new("application/json");

        if (createOnly)
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        else if (!string.IsNullOrWhiteSpace(expectedEtag))
            request.Headers.TryAddWithoutValidation("If-Match", expectedEtag);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var etag = response.Headers.ETag?.ToString();
        _log($"WebDAV final PUT response received. StatusCode={(int)response.StatusCode}.");

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            _log($"WebDAV final PUT finished: precondition failed. RemoteEtagPresent={etag is not null}.");
            return SyncPutResult.PreconditionFailed(etag);
        }

        response.EnsureSuccessStatusCode();
        _log($"WebDAV final PUT finished: created or updated. RemoteEtagPresent={etag is not null}.");
        return SyncPutResult.CreatedOrUpdated(etag);
    }

    private async Task<MoveTempObjectResult> MoveTempObjectToFinalPathAsync(
        string tempPath,
        string finalPath,
        CancellationToken cancellationToken)
    {
        _log("WebDAV MOVE request started.");
        using var request = new HttpRequestMessage(MoveMethod, CreateRemoteUri(tempPath));
        request.Headers.TryAddWithoutValidation("Destination", CreateRemoteUri(finalPath).ToString());
        request.Headers.TryAddWithoutValidation("Overwrite", "F");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var etag = response.Headers.ETag?.ToString();
        _log($"WebDAV MOVE response received. StatusCode={(int)response.StatusCode}.");

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            _log($"WebDAV MOVE finished: precondition failed. RemoteEtagPresent={etag is not null}.");
            return new MoveTempObjectResult(SyncPutResult.PreconditionFailed(etag), IsMoveUnsupported: false);
        }

        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            _log("WebDAV MOVE finished: operation unsupported.");
            return new MoveTempObjectResult(SyncPutResult.PreconditionFailed(etag), IsMoveUnsupported: true);
        }

        response.EnsureSuccessStatusCode();
        _log($"WebDAV MOVE finished: moved. RemoteEtagPresent={etag is not null}.");
        return new MoveTempObjectResult(
            SyncPutResult.CreatedOrUpdated(etag ?? await TryLoadRemoteEtagAsync(finalPath, cancellationToken).ConfigureAwait(false)),
            IsMoveUnsupported: false);
    }

    private async Task<string?> TryLoadRemoteEtagAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        _log("WebDAV ETag refresh started.");
        var directory = GetParentDirectory(relativePath);
        var objects = await ListAsync(directory, cancellationToken).ConfigureAwait(false);
        var etag = objects
            .FirstOrDefault(info => string.Equals(info.RelativePath, NormalizePath(relativePath), StringComparison.Ordinal))
            ?.ETag;
        _log($"WebDAV ETag refresh finished. RemoteEtagPresent={etag is not null}.");
        return etag;
    }

    private async Task TryDeleteTempObjectAsync(
        string tempPath,
        CancellationToken cancellationToken)
    {
        try
        {
            _log("WebDAV temp cleanup started.");
            await DeleteAsync(tempPath, expectedEtag: null, cancellationToken).ConfigureAwait(false);
            _log("WebDAV temp cleanup finished.");
        }
        catch (HttpRequestException)
        {
            _log("WebDAV temp cleanup failed: HTTP request error.");
        }
        catch (TaskCanceledException)
        {
            _log("WebDAV temp cleanup failed: operation canceled.");
        }
    }

    private async Task EnsureDirectoryAsync(string relativeDirectory, CancellationToken cancellationToken)
    {
        _log("WebDAV ensure directory request started.");
        using var request = new HttpRequestMessage(MkColMethod, CreateRemoteUri(relativeDirectory));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _log($"WebDAV ensure directory response received. StatusCode={(int)response.StatusCode}.");

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            _log("WebDAV ensure directory finished.");
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private List<SyncRemoteObjectInfo> ParsePropFindResponse(
        string xml,
        string relativeDirectory)
    {
        var normalizedDirectory = NormalizePath(relativeDirectory);
        var document = XDocument.Parse(xml);

        return document
            .Descendants(DavNamespace + "response")
            .Select(response => TryReadRemoteObjectInfo(response, normalizedDirectory))
            .Where(info => info is not null)
            .Cast<SyncRemoteObjectInfo>()
            .OrderBy(info => info.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private SyncRemoteObjectInfo? TryReadRemoteObjectInfo(XElement response, string relativeDirectory)
    {
        var href = response.Element(DavNamespace + "href")?.Value;
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var relativePath = TryGetRelativePath(href);
        if (relativePath is null || relativePath == relativeDirectory || !IsDirectChild(relativeDirectory, relativePath))
            return null;

        var prop = response
            .Elements(DavNamespace + "propstat")
            .Elements(DavNamespace + "prop")
            .FirstOrDefault();
        if (prop is null || IsCollection(prop))
            return null;

        return new SyncRemoteObjectInfo(
            relativePath,
            ReadString(prop, "getetag"),
            ReadDateTime(prop, "getlastmodified"),
            ReadContentLength(prop));
    }

    private string? TryGetRelativePath(string href)
    {
        Uri hrefUri;
        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
            hrefUri = absoluteUri;
        else if (Uri.TryCreate(_repositoryUri, href, out var relativeUri))
            hrefUri = relativeUri;
        else
            return null;

        var repositoryPath = Uri.UnescapeDataString(_repositoryUri.AbsolutePath).Trim('/');
        var objectPath = Uri.UnescapeDataString(hrefUri.AbsolutePath).Trim('/');

        if (string.IsNullOrEmpty(repositoryPath))
            return NormalizePath(objectPath);

        if (objectPath.Equals(repositoryPath, StringComparison.Ordinal))
            return string.Empty;

        var prefix = repositoryPath + "/";
        return objectPath.StartsWith(prefix, StringComparison.Ordinal)
            ? NormalizePath(objectPath[prefix.Length..])
            : null;
    }

    private Uri CreateRemoteUri(string relativePath)
    {
        return new Uri(_repositoryUri, NormalizePath(relativePath));
    }

    private static string CreateTempUploadPath(string finalPath)
    {
        var normalizedFinalPath = NormalizePath(finalPath);
        var safeFinalName = normalizedFinalPath
            .Replace('/', '-')
            .Replace('\\', '-');
        return $"{SyncRemoteRepositoryLayout.TempDirectory}/{Guid.NewGuid():N}-{safeFinalName}.json";
    }

    private static string GetParentDirectory(string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        var separatorIndex = normalizedPath.LastIndexOf('/');
        return separatorIndex <= 0
            ? string.Empty
            : normalizedPath[..separatorIndex];
    }

    private static bool IsDirectChild(string relativeDirectory, string relativePath)
    {
        var prefix = string.IsNullOrEmpty(relativeDirectory)
            ? string.Empty
            : relativeDirectory + "/";

        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return !relativePath[prefix.Length..].Contains('/', StringComparison.Ordinal);
    }

    private static bool IsCollection(XElement prop)
    {
        return prop
            .Element(DavNamespace + "resourcetype")
            ?.Element(DavNamespace + "collection") is not null;
    }

    private static string? ReadString(XElement prop, string localName)
    {
        var value = prop.Element(DavNamespace + localName)?.Value;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static DateTimeOffset? ReadDateTime(XElement prop, string localName)
    {
        var value = ReadString(prop, localName);
        if (value is null)
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static long? ReadContentLength(XElement prop)
    {
        var value = ReadString(prop, "getcontentlength");
        if (value is null)
            return null;

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            ? length
            : null;
    }

    private static string CreatePropFindBody()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:">
              <D:prop>
                <D:getetag />
                <D:getlastmodified />
                <D:getcontentlength />
                <D:resourcetype />
              </D:prop>
            </D:propfind>
            """;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith('/')
            ? uri
            : new Uri(value + "/", UriKind.Absolute);
    }

    private static string NormalizePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        return relativePath.Replace('\\', '/').Trim('/');
    }

    private sealed record MoveTempObjectResult(
        SyncPutResult Result,
        bool IsMoveUnsupported);
}
