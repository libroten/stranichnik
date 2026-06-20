using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Push;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Sync;

public sealed class SyncApplicationServiceFactory
{
    private readonly Func<HttpMessageHandler>? _httpMessageHandlerFactory;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly Action<string>? _log;
    private readonly ISyncActivityService? _syncActivityService;

    public SyncApplicationServiceFactory(
        Func<HttpMessageHandler>? httpMessageHandlerFactory = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        ISyncActivityService? syncActivityService = null)
    {
        _httpMessageHandlerFactory = httpMessageHandlerFactory;
        _clock = clock;
        _log = log;
        _syncActivityService = syncActivityService;
    }

    public SyncApplicationServiceFactoryResult Create(
        AppSettings settings,
        ISyncCredentialStore credentialStore,
        ISyncLocalStore localStore,
        ISyncOperationGate operationGate)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(localStore);
        ArgumentNullException.ThrowIfNull(operationGate);

        if (string.IsNullOrWhiteSpace(settings.Sync.WebDavUrl))
            return SyncApplicationServiceFactoryResult.MissingWebDavUrl();

        if (!Uri.TryCreate(settings.Sync.WebDavUrl, UriKind.Absolute, out var repositoryUri) ||
            repositoryUri.Scheme is not ("http" or "https"))
        {
            return SyncApplicationServiceFactoryResult.InvalidWebDavUrl();
        }

        if (string.IsNullOrWhiteSpace(settings.Sync.Username))
            return SyncApplicationServiceFactoryResult.MissingUsername();

        var credentials = credentialStore.Load();
        if (credentials is null)
            return SyncApplicationServiceFactoryResult.MissingCredentials();

        var serializer = new SystemTextSyncJsonSerializer();
        var contentHasher = new Sha256SyncContentHasher();
        var httpClient = CreateHttpClient(settings.Sync.Username, credentials.Password);
        var transport = new HttpWebDavSyncTransport(httpClient, repositoryUri, _log);
        var remoteReader = new SyncRemoteObjectReader(transport, serializer, contentHasher, _log);
        var identity = localStore.LoadSnapshot().Identity;
        var repositoryInitializer = new SyncRepositoryInitializer(
            transport,
            serializer,
            identity,
            clock: _clock,
            log: _log);
        var pullService = new SyncPullService(
            localStore,
            remoteReader,
            clock: _clock,
            log: _log);
        var pushService = new SyncPushService(
            localStore,
            transport,
            serializer,
            new SyncRemoteDtoMapper(serializer, contentHasher),
            clock: _clock,
            log: _log);

        return SyncApplicationServiceFactoryResult.Ready(new SyncApplicationService(
            repositoryInitializer,
            pullService,
            pushService,
            operationGate,
            ownedResource: httpClient,
            clock: _clock,
            log: _log,
            syncActivityService: _syncActivityService));
    }

    private HttpClient CreateHttpClient(string username, string password)
    {
        var httpClient = _httpMessageHandlerFactory is null
            ? new HttpClient()
            : new HttpClient(_httpMessageHandlerFactory());
        var authBytes = Encoding.UTF8.GetBytes(username + ":" + password);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(authBytes));

        return httpClient;
    }
}
