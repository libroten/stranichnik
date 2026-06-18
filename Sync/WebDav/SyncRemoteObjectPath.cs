using System;
using Stranichnik.Sync;

namespace Stranichnik.Sync.WebDav;

public static class SyncRemoteObjectPath
{
    private const string JsonExtension = ".json";

    public static string GetDirectory(SyncObjectKind kind)
    {
        return kind switch
        {
            SyncObjectKind.Item => SyncRemoteRepositoryLayout.ItemsDirectory,
            SyncObjectKind.IconAsset => SyncRemoteRepositoryLayout.IconAssetsDirectory,
            SyncObjectKind.SecretIconAsset => SyncRemoteRepositoryLayout.SecretIconAssetsDirectory,
            SyncObjectKind.CryptoProfile => SyncRemoteRepositoryLayout.CryptoProfilesDirectory,
            SyncObjectKind.SecretResetEvent => SyncRemoteRepositoryLayout.SecretResetEventsDirectory,
            SyncObjectKind.Device => SyncRemoteRepositoryLayout.DevicesDirectory,
            SyncObjectKind.Manifest => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported sync object kind.")
        };
    }

    public static string ToRelativePath(SyncObjectIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Id);

        if (identity.Kind == SyncObjectKind.Manifest)
            return SyncRemoteRepositoryLayout.ManifestPath;

        return $"{GetDirectory(identity.Kind)}/{Uri.EscapeDataString(identity.Id)}{JsonExtension}";
    }

    public static bool TryParse(string relativePath, out SyncObjectIdentity? identity)
    {
        identity = null;

        var normalizedPath = NormalizePath(relativePath);
        if (normalizedPath == SyncRemoteRepositoryLayout.ManifestPath)
        {
            identity = new SyncObjectIdentity(SyncObjectKind.Manifest, SyncRemoteRepositoryLayout.ManifestPath);
            return true;
        }

        var separatorIndex = normalizedPath.IndexOf('/', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex != normalizedPath.LastIndexOf('/'))
            return false;

        var directory = normalizedPath[..separatorIndex];
        var fileName = normalizedPath[(separatorIndex + 1)..];
        if (!fileName.EndsWith(JsonExtension, StringComparison.Ordinal) || fileName.Length == JsonExtension.Length)
            return false;

        var kind = ToObjectKind(directory);
        if (kind is null)
            return false;

        var escapedId = fileName[..^JsonExtension.Length];
        var id = Uri.UnescapeDataString(escapedId);
        if (string.IsNullOrWhiteSpace(id))
            return false;

        identity = new SyncObjectIdentity(kind.Value, id);
        return true;
    }

    private static SyncObjectKind? ToObjectKind(string directory)
    {
        return directory switch
        {
            SyncRemoteRepositoryLayout.ItemsDirectory => SyncObjectKind.Item,
            SyncRemoteRepositoryLayout.IconAssetsDirectory => SyncObjectKind.IconAsset,
            SyncRemoteRepositoryLayout.SecretIconAssetsDirectory => SyncObjectKind.SecretIconAsset,
            SyncRemoteRepositoryLayout.CryptoProfilesDirectory => SyncObjectKind.CryptoProfile,
            SyncRemoteRepositoryLayout.SecretResetEventsDirectory => SyncObjectKind.SecretResetEvent,
            SyncRemoteRepositoryLayout.DevicesDirectory => SyncObjectKind.Device,
            _ => null
        };
    }

    private static string NormalizePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        return relativePath.Replace('\\', '/').Trim('/');
    }
}
