using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;

namespace Stranichnik.DevResetSync;

internal static class Program
{
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly XNamespace DavNamespace = "DAV:";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal) ||
            args.Contains("-h", StringComparer.Ordinal))
        {
            PrintUsage();
            return 0;
        }

        var skipLaunch = args.Contains("--skip-launch", StringComparer.Ordinal);

        try
        {
            Console.WriteLine("Stranichnik dev sync reset started.");

            var currentSettings = AppSettingsService.Load();
            var syncSettings = currentSettings.Sync;
            var repositoryUri = ValidateRepositoryUri(syncSettings.WebDavUrl);
            var username = ValidateUsername(syncSettings.Username);
            var credentials = LoadCredentials();
            ValidateCredentialStorageKind(syncSettings.CredentialStorageKind);

            Console.WriteLine("Current sync settings loaded.");
            Console.WriteLine("Remote WebDAV cleanup started.");
            using var httpClient = CreateHttpClient(username, credentials);
            var cleaner = new WebDavDirectoryCleaner(httpClient, repositoryUri);
            var cleanupResult = await cleaner.DeleteDirectoryContentsAsync(CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(
                "Remote WebDAV cleanup finished. " +
                $"DeletedFiles={cleanupResult.DeletedFiles.ToString(CultureInfo.InvariantCulture)}; " +
                $"DeletedDirectories={cleanupResult.DeletedDirectories.ToString(CultureInfo.InvariantCulture)}.");

            Console.WriteLine("Local application data reset started.");
            ResetLocalApplicationData(syncSettings, credentials);
            Console.WriteLine("Local application data reset finished.");

            if (skipLaunch)
            {
                Console.WriteLine("Application launch skipped.");
                return 0;
            }

            Console.WriteLine("Launching Stranichnik with console logging.");
            return await LaunchApplicationAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or
                TaskCanceledException or XmlException)
        {
            Console.Error.WriteLine("Dev sync reset failed.");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project Tools/Stranichnik.DevResetSync");
        Console.WriteLine("  dotnet run --project Tools/Stranichnik.DevResetSync -- --skip-launch");
        Console.WriteLine();
        Console.WriteLine("The utility preserves configured WebDAV credentials, clears local app data,");
        Console.WriteLine("deletes all contents of the configured WebDAV directory, and launches the app");
        Console.WriteLine("with --print-logs-to-console.");
    }

    private static Uri ValidateRepositoryUri(string webDavUrl)
    {
        if (string.IsNullOrWhiteSpace(webDavUrl))
            throw new InvalidOperationException("WebDAV URL is not configured.");

        if (!Uri.TryCreate(webDavUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("WebDAV URL must be an absolute HTTP or HTTPS URL.");
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("WebDAV URL points to a root directory. Refusing to delete it.");

        return EnsureTrailingSlash(uri);
    }

    private static string ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("WebDAV username is not configured.");

        return username.Trim();
    }

    private static SyncCredentials LoadCredentials()
    {
        var credentialStore = SyncCredentialStoreFactory.CreateDefault();
        return credentialStore.Load() ??
            throw new InvalidOperationException("WebDAV password is not available in the configured credential store.");
    }

    private static void ValidateCredentialStorageKind(string credentialStorageKind)
    {
        var storageKind = SyncCredentialStorageKindNames.FromSettingsValue(credentialStorageKind);
        if (storageKind == SyncCredentialStorageKind.SessionOnly)
            throw new InvalidOperationException("WebDAV password is not persisted. Save sync settings in the app first.");
    }

    private static HttpClient CreateHttpClient(string username, SyncCredentials credentials)
    {
        var httpClient = new HttpClient();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{credentials.Password}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        return httpClient;
    }

    private static void ResetLocalApplicationData(SyncSettings sourceSyncSettings, SyncCredentials credentials)
    {
        Directory.CreateDirectory(AppDataPaths.AppDataDirectory);

        foreach (var filePath in Directory.EnumerateFiles(AppDataPaths.AppDataDirectory))
            File.Delete(filePath);

        foreach (var directoryPath in Directory.EnumerateDirectories(AppDataPaths.AppDataDirectory))
            Directory.Delete(directoryPath, recursive: true);

        var minimalSettings = new AppSettings
        {
            Sync = new SyncSettings
            {
                WebDavUrl = sourceSyncSettings.WebDavUrl.Trim(),
                Username = sourceSyncSettings.Username.Trim(),
                CredentialStorageKind = sourceSyncSettings.CredentialStorageKind,
                LastSuccessfulSyncAtUtc = null
            }
        };
        AppSettingsService.Save(minimalSettings);

        if (SyncCredentialStorageKindNames.FromSettingsValue(sourceSyncSettings.CredentialStorageKind) ==
            SyncCredentialStorageKind.ObfuscatedFile)
        {
            var fileStore = new ObfuscatedFileSyncCredentialStore(
                AppDataPaths.SyncCredentialsPath,
                _ => { });
            if (!fileStore.Save(minimalSettings.Sync.Username, credentials))
                throw new InvalidOperationException("Failed to recreate the local WebDAV credential file.");
        }
    }

    private static async Task<int> LaunchApplicationAsync(CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot() ??
            throw new InvalidOperationException("Could not find repository root with Stranichnik.csproj.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--print-logs-to-console");

        using var process = Process.Start(startInfo);

        if (process is null)
            throw new InvalidOperationException("Failed to start the application process.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string? FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var fromCurrentDirectory = FindRepositoryRoot(currentDirectory);
        if (fromCurrentDirectory is not null)
            return fromCurrentDirectory;

        return FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
    }

    private static string? FindRepositoryRoot(DirectoryInfo startDirectory)
    {
        for (var directory = startDirectory; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Stranichnik.csproj")))
                return directory.FullName;
        }

        return null;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith('/')
            ? uri
            : new Uri(value + "/", UriKind.Absolute);
    }

    private sealed class WebDavDirectoryCleaner
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _repositoryUri;

        public WebDavDirectoryCleaner(HttpClient httpClient, Uri repositoryUri)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(repositoryUri);

            _httpClient = httpClient;
            _repositoryUri = EnsureTrailingSlash(repositoryUri);
        }

        public async Task<WebDavCleanupResult> DeleteDirectoryContentsAsync(CancellationToken cancellationToken)
        {
            var result = new WebDavCleanupResult();
            var children = await ListChildrenAsync(_repositoryUri, cancellationToken).ConfigureAwait(false);

            foreach (var child in children)
                await DeleteEntryAsync(child, result, cancellationToken).ConfigureAwait(false);

            return result;
        }

        private async Task DeleteEntryAsync(
            WebDavEntry entry,
            WebDavCleanupResult result,
            CancellationToken cancellationToken)
        {
            if (entry.IsCollection)
            {
                var children = await ListChildrenAsync(entry.Uri, cancellationToken).ConfigureAwait(false);
                foreach (var child in children)
                    await DeleteEntryAsync(child, result, cancellationToken).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Delete, entry.Uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return;

            response.EnsureSuccessStatusCode();

            if (entry.IsCollection)
                result.DeletedDirectories++;
            else
                result.DeletedFiles++;
        }

        private async Task<List<WebDavEntry>> ListChildrenAsync(Uri directoryUri, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(PropFindMethod, EnsureTrailingSlash(directoryUri));
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent(CreatePropFindBody(), Encoding.UTF8, "application/xml");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return [];

            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParsePropFindResponse(xml, directoryUri);
        }

        private List<WebDavEntry> ParsePropFindResponse(string xml, Uri directoryUri)
        {
            var document = XDocument.Parse(xml);
            return document
                .Descendants(DavNamespace + "response")
                .Select(response => TryReadEntry(response, directoryUri))
                .Where(entry => entry is not null)
                .Cast<WebDavEntry>()
                .OrderBy(entry => entry.Uri.AbsoluteUri, StringComparer.Ordinal)
                .ToList();
        }

        private WebDavEntry? TryReadEntry(XElement response, Uri directoryUri)
        {
            var href = response.Element(DavNamespace + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
                return null;

            var entryUri = TryCreateSafeEntryUri(href, directoryUri);
            if (entryUri is null)
                return null;

            var prop = response
                .Elements(DavNamespace + "propstat")
                .Elements(DavNamespace + "prop")
                .FirstOrDefault();
            if (prop is null)
                return null;

            var isCollection = IsCollection(prop);
            return new WebDavEntry(
                isCollection ? EnsureTrailingSlash(entryUri) : entryUri,
                isCollection);
        }

        private Uri? TryCreateSafeEntryUri(string href, Uri directoryUri)
        {
            Uri hrefUri;
            if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
                hrefUri = absoluteUri;
            else if (Uri.TryCreate(_repositoryUri, href, out var relativeUri))
                hrefUri = relativeUri;
            else
                return null;

            if (!string.Equals(hrefUri.Scheme, _repositoryUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(hrefUri.Host, _repositoryUri.Host, StringComparison.OrdinalIgnoreCase) ||
                hrefUri.Port != _repositoryUri.Port)
            {
                return null;
            }

            var repositoryPath = Uri.UnescapeDataString(_repositoryUri.AbsolutePath).Trim('/');
            var currentDirectoryPath = Uri.UnescapeDataString(EnsureTrailingSlash(directoryUri).AbsolutePath).Trim('/');
            var entryPath = Uri.UnescapeDataString(hrefUri.AbsolutePath).Trim('/');
            if (entryPath.Equals(repositoryPath, StringComparison.Ordinal) ||
                entryPath.Equals(currentDirectoryPath, StringComparison.Ordinal))
            {
                return null;
            }

            var prefix = repositoryPath + "/";
            if (!entryPath.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            return hrefUri;
        }

        private static bool IsCollection(XElement prop)
        {
            return prop
                .Element(DavNamespace + "resourcetype")
                ?.Element(DavNamespace + "collection") is not null;
        }
    }

    private sealed record WebDavEntry(Uri Uri, bool IsCollection);

    private sealed class WebDavCleanupResult
    {
        public int DeletedFiles { get; set; }

        public int DeletedDirectories { get; set; }
    }

    private static string CreatePropFindBody()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:">
              <D:prop>
                <D:resourcetype />
              </D:prop>
            </D:propfind>
            """;
    }

}
