using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class HttpWebDavSyncTransportTests
{
    private static readonly byte[] PayloadBytes = [1, 2, 3];

    [Fact]
    public async Task EnsureRepositoryAsync_sends_mkcol_for_required_directories()
    {
        var requests = new List<CapturedHttpRequest>();
        using var httpClient = CreateClient(request =>
        {
            requests.Add(CaptureRequest(request));
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var transport = CreateTransport(httpClient);

        await transport.EnsureRepositoryAsync(CancellationToken.None);

        Assert.Equal(SyncRemoteRepositoryLayout.RequiredDirectories.Length, requests.Count);
        Assert.All(requests, request => Assert.Equal("MKCOL", request.Method.Method));
        Assert.Equal(
            SyncRemoteRepositoryLayout.RequiredDirectories.Select(directory => $"https://example.test/sync/{directory}"),
            requests.Select(request => request.RequestUri).ToArray());
    }

    [Fact]
    public async Task EnsureRepositoryAsync_treats_existing_directory_as_success()
    {
        using var httpClient = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        var transport = CreateTransport(httpClient);

        await transport.EnsureRepositoryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ListAsync_sends_propfind_and_parses_direct_file_children()
    {
        CapturedHttpRequest? capturedRequest = null;
        using var httpClient = CreateClient(request =>
        {
            capturedRequest = CaptureRequest(request);
            return CreateXmlResponse(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <D:multistatus xmlns:D="DAV:">
                  <D:response>
                    <D:href>/sync/items/</D:href>
                    <D:propstat>
                      <D:prop><D:resourcetype><D:collection /></D:resourcetype></D:prop>
                    </D:propstat>
                  </D:response>
                  <D:response>
                    <D:href>/sync/items/a.json</D:href>
                    <D:propstat>
                      <D:prop>
                        <D:getetag>"a"</D:getetag>
                        <D:getlastmodified>Sat, 13 Jun 2026 10:20:30 GMT</D:getlastmodified>
                        <D:getcontentlength>42</D:getcontentlength>
                        <D:resourcetype />
                      </D:prop>
                    </D:propstat>
                  </D:response>
                  <D:response>
                    <D:href>/sync/items/nested/b.json</D:href>
                    <D:propstat>
                      <D:prop>
                        <D:getetag>"nested"</D:getetag>
                        <D:getcontentlength>7</D:getcontentlength>
                        <D:resourcetype />
                      </D:prop>
                    </D:propstat>
                  </D:response>
                </D:multistatus>
                """);
        });
        var transport = CreateTransport(httpClient);

        var listed = await transport.ListAsync("items", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("PROPFIND", capturedRequest.Method.Method);
        Assert.Equal("1", capturedRequest.GetHeader("Depth").Single());
        var item = Assert.Single(listed);
        Assert.Equal("items/a.json", item.RelativePath);
        Assert.Equal("\"a\"", item.ETag);
        Assert.Equal(42, item.ContentLength);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 10, 20, 30, TimeSpan.Zero), item.LastModifiedUtc);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_not_found()
    {
        using var httpClient = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var transport = CreateTransport(httpClient);

        var loaded = await transport.GetAsync("items/missing.json", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetAsync_returns_response_bytes()
    {
        using var httpClient = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(PayloadBytes)
        });
        var transport = CreateTransport(httpClient);

        var loaded = await transport.GetAsync("items/item.json", CancellationToken.None);

        Assert.Equal(PayloadBytes, loaded);
    }

    [Fact]
    public async Task PutAsync_create_only_sends_if_none_match()
    {
        CapturedHttpRequest? capturedRequest = null;
        using var httpClient = CreateClient(request =>
        {
            capturedRequest = CaptureRequest(request);
            return CreatePutResponse(HttpStatusCode.Created, "\"new\"");
        });
        var transport = CreateTransport(httpClient);

        var result = await transport.PutAsync(
            "items/item.json",
            PayloadBytes,
            expectedEtag: null,
            createOnly: true,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Put, capturedRequest.Method);
        Assert.Equal("*", capturedRequest.GetHeader("If-None-Match").Single());
        Assert.Equal(SyncPutStatus.CreatedOrUpdated, result.Status);
        Assert.Equal("\"new\"", result.ETag);
    }

    [Fact]
    public async Task PutAsync_expected_etag_sends_if_match()
    {
        CapturedHttpRequest? capturedRequest = null;
        using var httpClient = CreateClient(request =>
        {
            capturedRequest = CaptureRequest(request);
            return CreatePutResponse(HttpStatusCode.NoContent, "\"updated\"");
        });
        var transport = CreateTransport(httpClient);

        var result = await transport.PutAsync(
            "items/item.json",
            PayloadBytes,
            expectedEtag: "\"old\"",
            createOnly: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("\"old\"", capturedRequest.GetHeader("If-Match").Single());
        Assert.Equal(SyncPutStatus.CreatedOrUpdated, result.Status);
        Assert.Equal("\"updated\"", result.ETag);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task PutAsync_returns_precondition_failed_for_conflict_statuses(HttpStatusCode statusCode)
    {
        using var httpClient = CreateClient(_ => CreatePutResponse(statusCode, "\"current\""));
        var transport = CreateTransport(httpClient);

        var result = await transport.PutAsync(
            "items/item.json",
            PayloadBytes,
            expectedEtag: "\"old\"",
            createOnly: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal(SyncPutStatus.PreconditionFailed, result.Status);
        Assert.Equal("\"current\"", result.ETag);
    }

    private static HttpWebDavSyncTransport CreateTransport(HttpClient httpClient)
    {
        return new HttpWebDavSyncTransport(httpClient, new Uri("https://example.test/sync/"));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler));
    }

    private static HttpResponseMessage CreateXmlResponse(string xml)
    {
        return new HttpResponseMessage((HttpStatusCode)207)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
    }

    private static HttpResponseMessage CreatePutResponse(HttpStatusCode statusCode, string etag)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    private static CapturedHttpRequest CaptureRequest(HttpRequestMessage request)
    {
        return new CapturedHttpRequest(
            request.Method,
            request.RequestUri?.ToString(),
            request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record CapturedHttpRequest(
        HttpMethod Method,
        string? RequestUri,
        Dictionary<string, string[]> Headers)
    {
        public string[] GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var values)
                ? values
                : [];
        }
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
