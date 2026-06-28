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
                $"DeletedDirectories={cleanupResult.DeletedDirectories.ToString(CultureInfo.InvariantCulture)}; " +
                $"RemoteResponses={cleanupResult.RemoteResponses.ToString(CultureInfo.InvariantCulture)}; " +
                $"AcceptedEntries={cleanupResult.AcceptedEntries.ToString(CultureInfo.InvariantCulture)}; " +
                $"SkippedSelfEntries={cleanupResult.SkippedSelfEntries.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedEntries={cleanupResult.RejectedEntries.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedOutsideDirectory={cleanupResult.RejectedOutsideDirectory.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedMissingProp={cleanupResult.RejectedMissingProp.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedMissingHref={cleanupResult.RejectedMissingHref.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedInvalidHref={cleanupResult.RejectedInvalidHref.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedUnsupportedHrefScheme={cleanupResult.RejectedUnsupportedHrefScheme.ToString(CultureInfo.InvariantCulture)}; " +
                $"RejectedOther={cleanupResult.RejectedOther.ToString(CultureInfo.InvariantCulture)}.");

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
            var children = await ListChildrenAsync(_repositoryUri, result, cancellationToken).ConfigureAwait(false);

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
                var children = await ListChildrenAsync(entry.Uri, result, cancellationToken).ConfigureAwait(false);
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

        private async Task<List<WebDavEntry>> ListChildrenAsync(
            Uri directoryUri,
            WebDavCleanupResult result,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(PropFindMethod, EnsureTrailingSlash(directoryUri));
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent(CreatePropFindBody(), Encoding.UTF8, "application/xml");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return [];

            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var effectiveDirectoryUri = EnsureTrailingSlash(response.RequestMessage?.RequestUri ?? directoryUri);
            return ParsePropFindResponse(xml, effectiveDirectoryUri, result);
        }

        private List<WebDavEntry> ParsePropFindResponse(
            string xml,
            Uri directoryUri,
            WebDavCleanupResult result)
        {
            var document = XDocument.Parse(xml);
            var entries = new List<WebDavEntry>();
            var responses = DescendantsByLocalName(document.Root, "response").ToList();
            var selfHref = responses
                .Select(response => TryReadHrefUri(response, directoryUri))
                .Where(readResult => readResult.Status == WebDavEntryReadStatus.Accepted)
                .Select(readResult => readResult.Entry?.Uri)
                .Where(uri => uri is not null)
                .Cast<Uri>()
                .OrderBy(uri => Uri.UnescapeDataString(uri.AbsolutePath).Trim('/').Length)
                .FirstOrDefault();

            foreach (var response in DescendantsByLocalName(document.Root, "response"))
            {
                result.RemoteResponses++;
                var readResult = TryReadEntry(response, directoryUri, selfHref);
                switch (readResult.Status)
                {
                    case WebDavEntryReadStatus.Accepted:
                        result.AcceptedEntries++;
                        entries.Add(readResult.Entry ?? throw new InvalidOperationException("Accepted entry is missing."));
                        break;
                    case WebDavEntryReadStatus.SkippedSelf:
                        result.SkippedSelfEntries++;
                        break;
                    case WebDavEntryReadStatus.OutsideDirectory:
                        result.RejectedEntries++;
                        result.RejectedOutsideDirectory++;
                        break;
                    case WebDavEntryReadStatus.MissingProp:
                        result.RejectedEntries++;
                        result.RejectedMissingProp++;
                        break;
                    case WebDavEntryReadStatus.MissingHref:
                        result.RejectedEntries++;
                        result.RejectedMissingHref++;
                        break;
                    case WebDavEntryReadStatus.InvalidHref:
                        result.RejectedEntries++;
                        result.RejectedInvalidHref++;
                        break;
                    case WebDavEntryReadStatus.UnsupportedHrefScheme:
                        result.RejectedEntries++;
                        result.RejectedUnsupportedHrefScheme++;
                        break;
                    case WebDavEntryReadStatus.Rejected:
                        result.RejectedEntries++;
                        result.RejectedOther++;
                        break;
                }
            }

            return entries
                .OrderBy(entry => entry.Uri.AbsoluteUri, StringComparer.Ordinal)
                .ToList();
        }

        private static WebDavEntryReadResult TryReadEntry(XElement response, Uri directoryUri, Uri? selfHref)
        {
            var entryUri = TryReadHrefUri(response, directoryUri);
            if (entryUri.Status != WebDavEntryReadStatus.Accepted)
                return new WebDavEntryReadResult(entryUri.Status, null);

            var uri = entryUri.Entry?.Uri ??
                throw new InvalidOperationException("Accepted href result is missing.");
            if (selfHref is not null && IsSameUriPath(uri, selfHref))
                return WebDavEntryReadResult.SkippedSelf();

            var prop = response
                .Elements()
                .Where(element => HasLocalName(element, "propstat"))
                .SelectMany(element => element.Elements())
                .Where(element => HasLocalName(element, "prop"))
                .FirstOrDefault();
            if (prop is null)
                return WebDavEntryReadResult.MissingProp();

            var isCollection = IsCollection(prop);
            if (isCollection && IsSameUriPath(uri, directoryUri))
                return WebDavEntryReadResult.SkippedSelf();

            return WebDavEntryReadResult.Accepted(new WebDavEntry(
                isCollection ? EnsureTrailingSlash(uri) : uri,
                isCollection));
        }

        private static WebDavEntryReadResult TryReadHrefUri(XElement response, Uri directoryUri)
        {
            var href = DescendantsByLocalName(response, "href").FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(href))
                return WebDavEntryReadResult.MissingHref();

            return TryCreateHrefUri(href, directoryUri);
        }

        private static WebDavEntryReadResult TryCreateHrefUri(string href, Uri directoryUri)
        {
            var trimmedHref = href.Trim();
            Uri hrefUri;
            if (Uri.TryCreate(trimmedHref, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                hrefUri = absoluteUri;
            }
            else if (Uri.TryCreate(directoryUri, trimmedHref, out var relativeUri))
            {
                hrefUri = relativeUri;
            }
            else
            {
                return WebDavEntryReadResult.InvalidHref();
            }

            if (hrefUri.Scheme != Uri.UriSchemeHttp && hrefUri.Scheme != Uri.UriSchemeHttps)
                return WebDavEntryReadResult.UnsupportedHrefScheme();

            return WebDavEntryReadResult.Accepted(new WebDavEntry(hrefUri, IsCollection: false));
        }

        private static bool IsSameUriPath(Uri left, Uri right)
        {
            return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
                left.Port == right.Port &&
                string.Equals(
                    Uri.UnescapeDataString(left.AbsolutePath).TrimEnd('/'),
                    Uri.UnescapeDataString(right.AbsolutePath).TrimEnd('/'),
                    StringComparison.Ordinal);
        }

        private static bool IsCollection(XElement prop)
        {
            return ChildByLocalName(prop, "resourcetype")
                ?.Elements()
                .Any(element => HasLocalName(element, "collection")) == true;
        }

        private static IEnumerable<XElement> DescendantsByLocalName(XElement? root, string localName)
        {
            return root is null
                ? []
                : root.Descendants().Where(element => HasLocalName(element, localName));
        }

        private static XElement? ChildByLocalName(XElement element, string localName)
        {
            return element.Elements().FirstOrDefault(child => HasLocalName(child, localName));
        }

        private static bool HasLocalName(XElement element, string localName)
        {
            return string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record WebDavEntry(Uri Uri, bool IsCollection);

    private sealed record WebDavEntryReadResult(
        WebDavEntryReadStatus Status,
        WebDavEntry? Entry)
    {
        public static WebDavEntryReadResult Accepted(WebDavEntry entry)
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.Accepted, entry);
        }

        public static WebDavEntryReadResult SkippedSelf()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.SkippedSelf, null);
        }

        public static WebDavEntryReadResult OutsideDirectory()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.OutsideDirectory, null);
        }

        public static WebDavEntryReadResult MissingProp()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.MissingProp, null);
        }

        public static WebDavEntryReadResult MissingHref()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.MissingHref, null);
        }

        public static WebDavEntryReadResult InvalidHref()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.InvalidHref, null);
        }

        public static WebDavEntryReadResult UnsupportedHrefScheme()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.UnsupportedHrefScheme, null);
        }

        public static WebDavEntryReadResult Rejected()
        {
            return new WebDavEntryReadResult(WebDavEntryReadStatus.Rejected, null);
        }
    }

    private enum WebDavEntryReadStatus
    {
        Accepted,
        SkippedSelf,
        OutsideDirectory,
        MissingProp,
        MissingHref,
        InvalidHref,
        UnsupportedHrefScheme,
        Rejected
    }

    private sealed class WebDavCleanupResult
    {
        public int DeletedFiles { get; set; }

        public int DeletedDirectories { get; set; }

        public int RemoteResponses { get; set; }

        public int AcceptedEntries { get; set; }

        public int SkippedSelfEntries { get; set; }

        public int RejectedEntries { get; set; }

        public int RejectedOutsideDirectory { get; set; }

        public int RejectedMissingProp { get; set; }

        public int RejectedMissingHref { get; set; }

        public int RejectedInvalidHref { get; set; }

        public int RejectedUnsupportedHrefScheme { get; set; }

        public int RejectedOther { get; set; }
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
