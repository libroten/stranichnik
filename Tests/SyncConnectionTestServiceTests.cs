using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Settings;
using Stranichnik.Sync;
using Stranichnik.Sync.Credentials;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncConnectionTestServiceTests
{
    [Theory]
    [InlineData(false, "https://example.invalid/sync/", "user", true, SyncConnectionTestStatus.Disabled)]
    [InlineData(true, "", "user", true, SyncConnectionTestStatus.MissingWebDavUrl)]
    [InlineData(true, "not-a-url", "user", true, SyncConnectionTestStatus.InvalidWebDavUrl)]
    [InlineData(true, "file:///tmp/sync", "user", true, SyncConnectionTestStatus.InvalidWebDavUrl)]
    [InlineData(true, "https://example.invalid/sync/", "", true, SyncConnectionTestStatus.MissingUsername)]
    [InlineData(true, "https://example.invalid/sync/", "user", false, SyncConnectionTestStatus.MissingCredentials)]
    public async Task TestAsync_returns_configuration_status(
        bool isEnabled,
        string webDavUrl,
        string username,
        bool hasCredentials,
        SyncConnectionTestStatus expectedStatus)
    {
        var credentialStore = CreateCredentialStore(hasCredentials);
        var service = new SyncConnectionTestService(log: _ => { });

        var result = await service.TestAsync(
            CreateSettings(isEnabled, webDavUrl, username),
            credentialStore,
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public async Task TestAsync_succeeds_and_sends_basic_auth_header()
    {
        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)207));
        var service = CreateService(handler);

        var result = await service.TestAsync(
            CreateSettings(isEnabled: true, webDavUrl: "https://example.invalid/sync/", username: "user"),
            CreateCredentialStore(hasCredentials: true),
            CancellationToken.None);

        Assert.Equal(SyncConnectionTestStatus.Succeeded, result.Status);
        Assert.Contains(handler.AuthorizationHeaders, header => header == "Basic dXNlcjpzZWNyZXQ=");
        Assert.All(handler.Methods, method => Assert.Equal("PROPFIND", method));
        Assert.All(handler.DepthHeaders, depth => Assert.Equal("0", depth));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TestAsync_maps_rejected_credentials(HttpStatusCode statusCode)
    {
        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var service = CreateService(handler);

        var result = await service.TestAsync(
            CreateSettings(isEnabled: true, webDavUrl: "https://example.invalid/sync/", username: "user"),
            CreateCredentialStore(hasCredentials: true),
            CancellationToken.None);

        Assert.Equal(SyncConnectionTestStatus.WrongCredentials, result.Status);
    }

    [Fact]
    public async Task TestAsync_maps_remote_failure()
    {
        using var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        var result = await service.TestAsync(
            CreateSettings(isEnabled: true, webDavUrl: "https://example.invalid/sync/", username: "user"),
            CreateCredentialStore(hasCredentials: true),
            CancellationToken.None);

        Assert.Equal(SyncConnectionTestStatus.RemoteUnavailable, result.Status);
    }

    private static SyncConnectionTestService CreateService(RecordingHttpMessageHandler handler)
    {
        return new SyncConnectionTestService(() => handler, log: _ => { });
    }

    private static InMemorySyncCredentialStore CreateCredentialStore(bool hasCredentials)
    {
        var credentialStore = new InMemorySyncCredentialStore();
        if (hasCredentials)
            credentialStore.SaveForSession(new SyncCredentials("secret"));

        return credentialStore;
    }

    private static AppSettings CreateSettings(
        bool isEnabled,
        string webDavUrl,
        string username)
    {
        return new AppSettings
        {
            Sync = new SyncSettings
            {
                IsEnabled = isEnabled,
                WebDavUrl = webDavUrl,
                Username = username
            }
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<string> AuthorizationHeaders { get; } = [];
        public List<string> Methods { get; } = [];
        public List<string?> DepthHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            DepthHeaders.Add(request.Headers.TryGetValues("Depth", out var values)
                ? string.Join(",", values)
                : null);

            if (request.Headers.Authorization is not null)
                AuthorizationHeaders.Add(request.Headers.Authorization.ToString());

            return Task.FromResult(_responseFactory(request));
        }
    }
}
