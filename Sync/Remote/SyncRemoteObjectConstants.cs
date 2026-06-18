namespace Stranichnik.Sync.Remote;

public static class SyncRemoteObjectConstants
{
    public const int FormatVersion = 1;

    public const string ManifestSchema = "stranichnik.sync.manifest";
    public const string ItemSchema = "stranichnik.sync.item";
    public const string IconAssetSchema = "stranichnik.sync.iconAsset";
    public const string SecretIconAssetSchema = "stranichnik.sync.secretIconAsset";
    public const string CryptoProfileSchema = "stranichnik.sync.cryptoProfile";
    public const string SecretResetEventSchema = "stranichnik.sync.secretResetEvent";
    public const string DeviceSchema = "stranichnik.sync.device";

    public const string ContentHashPlaceholder = "sha256:pending";

    public const string FolderKind = "folder";
    public const string BookmarkKind = "bookmark";
}
