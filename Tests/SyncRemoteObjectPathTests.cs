using Stranichnik.Sync;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncRemoteObjectPathTests
{
    [Theory]
    [InlineData(SyncObjectKind.Item, "bookmark", "items/bookmark.json")]
    [InlineData(SyncObjectKind.IconAsset, "icon", "icon-assets/icon.json")]
    [InlineData(SyncObjectKind.SecretIconAsset, "secret-icon", "secret-icon-assets/secret-icon.json")]
    [InlineData(SyncObjectKind.CryptoProfile, "generation", "crypto-profiles/generation.json")]
    [InlineData(SyncObjectKind.SecretResetEvent, "generation", "secret-reset-events/generation.json")]
    [InlineData(SyncObjectKind.Device, "device", "devices/device.json")]
    public void ToRelativePath_maps_identity_to_remote_path(
        SyncObjectKind kind,
        string id,
        string expectedPath)
    {
        var path = SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(kind, id));

        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ToRelativePath_maps_manifest_to_manifest_path()
    {
        var path = SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(
            SyncObjectKind.Manifest,
            "manifest"));

        Assert.Equal(SyncRemoteRepositoryLayout.ManifestPath, path);
    }

    [Fact]
    public void ToRelativePath_escapes_id_as_single_path_segment()
    {
        var path = SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(
            SyncObjectKind.Item,
            "folder/item"));

        Assert.Equal("items/folder%2Fitem.json", path);
    }

    [Theory]
    [InlineData("items/bookmark.json", SyncObjectKind.Item, "bookmark")]
    [InlineData("/items/bookmark.json", SyncObjectKind.Item, "bookmark")]
    [InlineData("items\\bookmark.json", SyncObjectKind.Item, "bookmark")]
    [InlineData("icon-assets/icon.json", SyncObjectKind.IconAsset, "icon")]
    [InlineData("secret-icon-assets/secret-icon.json", SyncObjectKind.SecretIconAsset, "secret-icon")]
    [InlineData("crypto-profiles/generation.json", SyncObjectKind.CryptoProfile, "generation")]
    [InlineData("secret-reset-events/generation.json", SyncObjectKind.SecretResetEvent, "generation")]
    [InlineData("devices/device.json", SyncObjectKind.Device, "device")]
    public void TryParse_maps_remote_path_to_identity(
        string path,
        SyncObjectKind expectedKind,
        string expectedId)
    {
        var parsed = SyncRemoteObjectPath.TryParse(path, out var identity);

        Assert.True(parsed);
        Assert.NotNull(identity);
        Assert.Equal(expectedKind, identity.Kind);
        Assert.Equal(expectedId, identity.Id);
    }

    [Fact]
    public void TryParse_maps_manifest_path_to_manifest_identity()
    {
        var parsed = SyncRemoteObjectPath.TryParse(SyncRemoteRepositoryLayout.ManifestPath, out var identity);

        Assert.True(parsed);
        Assert.NotNull(identity);
        Assert.Equal(SyncObjectKind.Manifest, identity.Kind);
        Assert.Equal(SyncRemoteRepositoryLayout.ManifestPath, identity.Id);
    }

    [Fact]
    public void TryParse_unescapes_id()
    {
        var parsed = SyncRemoteObjectPath.TryParse("items/folder%2Fitem.json", out var identity);

        Assert.True(parsed);
        Assert.NotNull(identity);
        Assert.Equal("folder/item", identity.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("items")]
    [InlineData("items/bookmark")]
    [InlineData("items/nested/bookmark.json")]
    [InlineData("unknown/bookmark.json")]
    [InlineData("items/.json")]
    public void TryParse_rejects_unsupported_paths(string path)
    {
        var parsed = SyncRemoteObjectPath.TryParse(path, out var identity);

        Assert.False(parsed);
        Assert.Null(identity);
    }
}
