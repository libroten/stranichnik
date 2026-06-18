using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;

namespace Stranichnik.Sync;

public sealed class SyncConnectionTestService
{
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");

    private readonly Func<HttpMessageHandler>? _httpMessageHandlerFactory;
    private readonly Action<string> _log;

    public SyncConnectionTestService(
        Func<HttpMessageHandler>? httpMessageHandlerFactory = null,
        Action<string>? log = null)
    {
        _httpMessageHandlerFactory = httpMessageHandlerFactory;
        _log = log ?? Logs.Print;
    }

    public async Task<SyncConnectionTestResult> TestAsync(
        AppSettings settings,
        ISyncCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialStore);

        var validationResult = Validate(settings, credentialStore, out var repositoryUri, out var credentials);
        if (validationResult is not null)
            return validationResult;

        try
        {
            _log("Sync connection test started.");
            using var httpClient = CreateHttpClient(settings.Sync.Username, credentials!.Password);
            using var request = new HttpRequestMessage(PropFindMethod, repositoryUri);
            request.Headers.TryAddWithoutValidation("Depth", "0");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            _log("Sync connection test succeeded.");
            return SyncConnectionTestResult.Succeeded();
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _log("Sync connection test failed: credentials rejected.");
            return SyncConnectionTestResult.WrongCredentials();
        }
        catch (HttpRequestException)
        {
            _log("Sync connection test failed: remote unavailable.");
            return SyncConnectionTestResult.RemoteUnavailable();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log("Sync connection test failed: request timed out.");
            return SyncConnectionTestResult.RemoteUnavailable();
        }
    }

    private static SyncConnectionTestResult? Validate(
        AppSettings settings,
        ISyncCredentialStore credentialStore,
        out Uri repositoryUri,
        out SyncCredentials? credentials)
    {
        repositoryUri = new Uri("https://example.invalid/", UriKind.Absolute);
        credentials = null;

        if (!settings.Sync.IsEnabled)
            return SyncConnectionTestResult.Disabled();

        if (string.IsNullOrWhiteSpace(settings.Sync.WebDavUrl))
            return SyncConnectionTestResult.MissingWebDavUrl();

        if (!Uri.TryCreate(settings.Sync.WebDavUrl, UriKind.Absolute, out var parsedRepositoryUri) ||
            parsedRepositoryUri.Scheme is not ("http" or "https"))
        {
            return SyncConnectionTestResult.InvalidWebDavUrl();
        }

        if (string.IsNullOrWhiteSpace(settings.Sync.Username))
            return SyncConnectionTestResult.MissingUsername();

        var loadedCredentials = credentialStore.Load();
        if (loadedCredentials is null)
            return SyncConnectionTestResult.MissingCredentials();

        repositoryUri = parsedRepositoryUri;
        credentials = loadedCredentials;
        return null;
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
