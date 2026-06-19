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
using Stranichnik.Sync;

namespace Stranichnik.Sync.WebDav;

public sealed class HttpWebDavSyncTransport : IWebDavSyncTransport
{
    private static readonly HttpMethod MkColMethod = new("MKCOL");
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly XNamespace DavNamespace = "DAV:";

    private readonly HttpClient _httpClient;
    private readonly Uri _repositoryUri;

    public HttpWebDavSyncTransport(HttpClient httpClient, Uri repositoryUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(repositoryUri);

        _httpClient = httpClient;
        _repositoryUri = EnsureTrailingSlash(repositoryUri);
    }

    public async Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in SyncRemoteRepositoryLayout.RequiredDirectories)
            await EnsureDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncRemoteObjectInfo>> ListAsync(
        string relativeDirectory,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            PropFindMethod,
            CreateRemoteUri(relativeDirectory));
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = new StringContent(CreatePropFindBody(), Encoding.UTF8, "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParsePropFindResponse(xml, relativeDirectory);
    }

    public async Task<byte[]?> GetAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            CreateRemoteUri(relativePath),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncPutResult> PutAsync(
        string relativePath,
        byte[] bytes,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var request = new HttpRequestMessage(HttpMethod.Put, CreateRemoteUri(relativePath));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new("application/json");

        if (createOnly)
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        else if (!string.IsNullOrWhiteSpace(expectedEtag))
            request.Headers.TryAddWithoutValidation("If-Match", expectedEtag);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var etag = response.Headers.ETag?.ToString();

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            return SyncPutResult.PreconditionFailed(etag);

        response.EnsureSuccessStatusCode();
        return SyncPutResult.CreatedOrUpdated(etag);
    }

    public async Task<SyncDeleteResult> DeleteAsync(
        string relativePath,
        string? expectedEtag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, CreateRemoteUri(relativePath));
        if (!string.IsNullOrWhiteSpace(expectedEtag))
            request.Headers.TryAddWithoutValidation("If-Match", expectedEtag);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var etag = response.Headers.ETag?.ToString();

        if (response.StatusCode == HttpStatusCode.NotFound)
            return SyncDeleteResult.DeletedOrMissing();

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            return SyncDeleteResult.PreconditionFailed(etag);

        response.EnsureSuccessStatusCode();
        return SyncDeleteResult.DeletedOrMissing();
    }

    private async Task EnsureDirectoryAsync(string relativeDirectory, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(MkColMethod, CreateRemoteUri(relativeDirectory));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
            return;

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
}
