namespace Stranichnik.Sync.WebDav;

public static class SyncRemoteRepositoryLayout
{
    public const string ManifestPath = "manifest.json";
    public const string DevicesDirectory = "devices";
    public const string ItemsDirectory = "items";
    public const string IconAssetsDirectory = "icon-assets";
    public const string SecretIconAssetsDirectory = "secret-icon-assets";
    public const string CryptoProfilesDirectory = "crypto-profiles";
    public const string SecretResetEventsDirectory = "secret-reset-events";

    public static readonly string[] RequiredDirectories =
    [
        DevicesDirectory,
        ItemsDirectory,
        IconAssetsDirectory,
        SecretIconAssetsDirectory,
        CryptoProfilesDirectory,
        SecretResetEventsDirectory
    ];
}
