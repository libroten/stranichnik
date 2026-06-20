using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Settings;
using Stranichnik.Sync;
using Stranichnik.Sync.Credentials;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncApplicationServiceFactoryTests
{
    [Theory]
    [InlineData("", "user", true, SyncApplicationServiceFactoryStatus.MissingWebDavUrl)]
    [InlineData("not-a-url", "user", true, SyncApplicationServiceFactoryStatus.InvalidWebDavUrl)]
    [InlineData("file:///tmp/sync", "user", true, SyncApplicationServiceFactoryStatus.InvalidWebDavUrl)]
    [InlineData("https://example.invalid/sync/", "", true, SyncApplicationServiceFactoryStatus.MissingUsername)]
    [InlineData("https://example.invalid/sync/", "user", false, SyncApplicationServiceFactoryStatus.MissingCredentials)]
    public void Create_returns_configuration_status(
        string webDavUrl,
        string username,
        bool hasCredentials,
        SyncApplicationServiceFactoryStatus expectedStatus)
    {
        var credentialStore = new InMemorySyncCredentialStore();
        if (hasCredentials)
            credentialStore.SaveForSession(new SyncCredentials("secret"));

        var result = CreateFactory().Create(
            CreateSettings(webDavUrl, username),
            credentialStore,
            new FakeSyncLocalStore(),
            new SyncOperationGate());

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Service);
    }

    [Fact]
    public async Task Create_ready_service_sends_basic_auth_header()
    {
        using var handler = new RecordingHttpMessageHandler();
        var credentialStore = new InMemorySyncCredentialStore();
        credentialStore.SaveForSession(new SyncCredentials("secret"));
        var result = CreateFactory(() => handler).Create(
            CreateSettings(webDavUrl: "https://example.invalid/sync/", username: "user"),
            credentialStore,
            new FakeSyncLocalStore(),
            new SyncOperationGate());

        Assert.Equal(SyncApplicationServiceFactoryStatus.Ready, result.Status);
        using var service = Assert.IsType<SyncApplicationService>(result.Service);

        await service.SyncNowAsync(CancellationToken.None);

        Assert.Contains(handler.AuthorizationHeaders, header => header == "Basic dXNlcjpzZWNyZXQ=");
    }

    private static SyncApplicationServiceFactory CreateFactory(
        Func<HttpMessageHandler>? httpMessageHandlerFactory = null)
    {
        return new SyncApplicationServiceFactory(
            httpMessageHandlerFactory,
            clock: () => Now,
            log: _ => { });
    }

    private static AppSettings CreateSettings(string webDavUrl, string username)
    {
        return new AppSettings
        {
            Sync = new SyncSettings
            {
                WebDavUrl = webDavUrl,
                Username = username
            }
        };
    }

    private sealed class FakeSyncLocalStore : ISyncLocalStore
    {
        public SyncLocalSnapshot LoadSnapshot()
        {
            return new SyncLocalSnapshot(
                new SyncLocalIdentity("database-id", "device-id"),
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }

        public void ApplyRemoteChanges(SyncApplyBatch batch)
        {
        }

        public void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc)
        {
        }

        public void MarkUploaded(
            SyncObjectIdentity identity,
            string? remoteEtag,
            string contentHash,
            DateTimeOffset syncedAtUtc)
        {
        }

        public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
        {
        }

        public void MarkDirty(SyncObjectIdentity identity)
        {
        }

        public void MarkQuarantinedRemoteObject(
            string objectKind,
            string relativePath,
            string? remoteEtag,
            string? contentHash,
            string reasonCode,
            DateTimeOffset seenAtUtc)
        {
        }

        public void ClearQuarantinedRemoteObject(string id)
        {
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<string> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is not null)
                AuthorizationHeaders.Add(request.Headers.Authorization.ToString());

            var response = request.Method.Method switch
            {
                "MKCOL" => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
                "PUT" => CreateResponse(HttpStatusCode.Created, "\"etag\""),
                "MOVE" => CreateResponse(HttpStatusCode.Created, "\"etag\""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };

            return Task.FromResult(response);
        }

        private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string etag)
        {
            var response = new HttpResponseMessage(statusCode);
            response.Headers.TryAddWithoutValidation("ETag", etag);
            return response;
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
