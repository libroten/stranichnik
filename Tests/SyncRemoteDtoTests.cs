using System;
using System.Text.Json;
using Stranichnik.Sync.Remote;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncRemoteDtoTests
{
    [Fact]
    public void ManifestDto_round_trips_with_remote_json_names()
    {
        var manifest = new SyncManifestDto(
            SyncRemoteObjectConstants.ManifestSchema,
            SyncRemoteObjectConstants.FormatVersion,
            "repository",
            CreatedAt,
            "device",
            MinimumAppSyncVersion: 1);

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<SyncManifestDto>(json);

        Assert.Contains("\"repositoryId\"", json, StringComparison.Ordinal);
        Assert.NotNull(deserialized);
        Assert.Equal(manifest, deserialized);
        SyncRemoteObjectValidator.Validate(deserialized);
    }

    [Fact]
    public void ItemDto_round_trips_normal_bookmark()
    {
        var item = CreateNormalBookmark();

        var json = JsonSerializer.Serialize(item);
        var deserialized = JsonSerializer.Deserialize<SyncItemDto>(json);

        Assert.Contains("\"isSecret\":false", json, StringComparison.Ordinal);
        Assert.NotNull(deserialized);
        Assert.Equal(item, deserialized);
        SyncRemoteObjectValidator.Validate(deserialized);
    }

    [Fact]
    public void Validator_rejects_folder_without_title()
    {
        var item = CreateNormalFolder() with
        {
            Title = null
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    [Fact]
    public void Validator_rejects_icon_reference_without_asset_id()
    {
        var item = CreateNormalBookmark() with
        {
            IconAssetRef = new SyncAssetReferenceDto(
                AssetId: null!,
                SourceHashAlgorithm: "sha256",
                SourceHash: "source-hash")
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    [Fact]
    public void Validator_rejects_secret_icon_reference_without_asset_id()
    {
        var item = CreateSecretBookmark() with
        {
            SecretIconAssetRef = new SyncAssetReferenceDto(
                AssetId: null!,
                SourceHashAlgorithm: "sha256",
                SourceHash: "source-hash")
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    [Fact]
    public void Validator_rejects_unsupported_format_version()
    {
        var item = CreateNormalBookmark() with
        {
            FormatVersion = 999
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    [Fact]
    public void Validator_rejects_manifest_with_missing_required_field()
    {
        const string Json = """
            {
              "schema": "stranichnik.sync.manifest",
              "formatVersion": 1,
              "createdAtUtc": "2026-01-01T00:00:00+00:00",
              "createdByDeviceId": "device",
              "minimumAppSyncVersion": 1
            }
            """;

        var manifest = JsonSerializer.Deserialize<SyncManifestDto>(Json);

        Assert.NotNull(manifest);
        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(manifest));
    }

    [Fact]
    public void Validator_rejects_secret_bookmark_with_plaintext_title_or_url()
    {
        var item = CreateSecretBookmark() with
        {
            Title = "Secret title"
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    [Fact]
    public void Validator_accepts_secret_bookmark_without_plaintext_title_or_url()
    {
        var item = CreateSecretBookmark();

        SyncRemoteObjectValidator.Validate(item);

        Assert.Null(item.Title);
        Assert.Null(item.Url);
    }

    [Fact]
    public void Validator_rejects_normal_bookmark_with_secret_payload_fields()
    {
        var item = CreateNormalBookmark() with
        {
            EncryptedPayload = "payload"
        };

        Assert.Throws<SyncRemoteObjectValidationException>(() =>
            SyncRemoteObjectValidator.Validate(item));
    }

    private static SyncItemDto CreateNormalBookmark()
    {
        return new(
            Schema: SyncRemoteObjectConstants.ItemSchema,
            FormatVersion: SyncRemoteObjectConstants.FormatVersion,
            Id: "bookmark",
            ParentId: null,
            Kind: SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: "Example",
            Url: "https://example.com/",
            IsSecret: false,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: null,
            EncryptionNonce: null,
            CryptoProfileSecretGenerationId: null,
            SecretPayloadFormatVersion: null,
            CreatedAtUtc: CreatedAt,
            UpdatedAtUtc: CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:content");
    }

    private static SyncItemDto CreateNormalFolder()
    {
        return new(
            Schema: SyncRemoteObjectConstants.ItemSchema,
            FormatVersion: SyncRemoteObjectConstants.FormatVersion,
            Id: "folder",
            ParentId: null,
            Kind: SyncRemoteObjectConstants.FolderKind,
            SortOrder: 1000,
            Title: "Folder",
            Url: null,
            IsSecret: false,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: null,
            EncryptionNonce: null,
            CryptoProfileSecretGenerationId: null,
            SecretPayloadFormatVersion: null,
            CreatedAtUtc: CreatedAt,
            UpdatedAtUtc: CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:content");
    }

    private static SyncItemDto CreateSecretBookmark()
    {
        return new(
            Schema: SyncRemoteObjectConstants.ItemSchema,
            FormatVersion: SyncRemoteObjectConstants.FormatVersion,
            Id: "secret-bookmark",
            ParentId: null,
            Kind: SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: null,
            Url: null,
            IsSecret: true,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: Convert.ToBase64String([1, 2, 3]),
            EncryptionNonce: Convert.ToBase64String([4, 5, 6]),
            CryptoProfileSecretGenerationId: "generation",
            SecretPayloadFormatVersion: 1,
            CreatedAtUtc: CreatedAt,
            UpdatedAtUtc: CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:content");
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
