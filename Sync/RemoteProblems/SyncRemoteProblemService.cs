using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Sync.RemoteProblems;

public sealed class SyncRemoteProblemService : IDisposable
{
    private readonly ISyncLocalStore _localStore;
    private readonly IWebDavSyncTransport _transport;
    private readonly IDisposable? _ownedResource;
    private readonly Action<string> _log;
    private readonly ISyncActivityService? _syncActivityService;

    public SyncRemoteProblemService(
        ISyncLocalStore localStore,
        IWebDavSyncTransport transport,
        IDisposable? ownedResource = null,
        Action<string>? log = null,
        ISyncActivityService? syncActivityService = null)
    {
        ArgumentNullException.ThrowIfNull(localStore);
        ArgumentNullException.ThrowIfNull(transport);

        _localStore = localStore;
        _transport = transport;
        _ownedResource = ownedResource;
        _log = log ?? Logs.Print;
        _syncActivityService = syncActivityService;
    }

    public async Task<SyncRemoteProblemDeleteResult> DeleteRemoteProblemAsync(
        SyncQuarantinedRemoteObjectRecord problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        _log("Sync remote problem delete started.");
        using var syncActivity = _syncActivityService?.BeginOperation();
        var result = await _transport
            .DeleteAsync(problem.RelativePath, problem.RemoteEtag, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == SyncDeleteStatus.PreconditionFailed)
        {
            _log("Sync remote problem delete stopped: remote object changed.");
            return SyncRemoteProblemDeleteResult.RemoteChanged();
        }

        _localStore.ClearQuarantinedRemoteObject(problem.Id);
        _log("Sync remote problem delete finished.");
        return SyncRemoteProblemDeleteResult.DeletedOrMissing();
    }

    public void Dispose()
    {
        _ownedResource?.Dispose();
    }

    public static SyncRemoteProblemService? TryCreate(
        AppSettings settings,
        ISyncCredentialStore credentialStore,
        ISyncLocalStore? localStore,
        ISyncActivityService? syncActivityService = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialStore);

        if (localStore is null ||
            !settings.Sync.IsEnabled ||
            string.IsNullOrWhiteSpace(settings.Sync.WebDavUrl) ||
            string.IsNullOrWhiteSpace(settings.Sync.Username) ||
            !Uri.TryCreate(settings.Sync.WebDavUrl, UriKind.Absolute, out var repositoryUri) ||
            repositoryUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var credentials = credentialStore.Load();
        if (credentials is null)
            return null;

        var httpClient = CreateHttpClient(settings.Sync.Username, credentials.Password);
        return new SyncRemoteProblemService(
            localStore,
            new HttpWebDavSyncTransport(httpClient, repositoryUri),
            ownedResource: httpClient,
            syncActivityService: syncActivityService);
    }

    private static HttpClient CreateHttpClient(string username, string password)
    {
        var httpClient = new HttpClient();
        var authBytes = Encoding.UTF8.GetBytes(username + ":" + password);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(authBytes));

        return httpClient;
    }
}
