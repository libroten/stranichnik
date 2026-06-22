using System;
using System.Collections.Generic;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Push;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncRemoteDtoMapperTests
{
    [Fact]
    public void ToDto_maps_normal_bookmark_with_regular_icon_reference()
    {
        var mapper = CreateMapper();
        var iconAsset = CreateIconAsset("icon");
        var item = CreateNormalItem("bookmark", iconAssetId: "icon");

        var dto = mapper.ToDto(
            item,
            new Dictionary<long, string>(),
            new Dictionary<string, BookmarkIconAssetRecord> { ["icon"] = iconAsset.Asset },
            new Dictionary<string, SecretIconAssetRecord>());

        SyncRemoteObjectValidator.Validate(dto);
        Assert.Equal("bookmark", dto.Id);
        Assert.False(dto.IsSecret);
        Assert.Equal("Title", dto.Title);
        Assert.Equal("https://example.com/", dto.Url);
        Assert.Equal("icon", dto.IconAssetRef?.AssetId);
        Assert.StartsWith("sha256:", dto.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDto_maps_secret_bookmark_without_plaintext_fields()
    {
        var mapper = CreateMapper();
        var secretIconAsset = CreateSecretIconAsset("secret-icon", "generation");
        var item = CreateSecretItem("secret-bookmark", cryptoProfileId: 1, secretIconAssetId: "secret-icon");

        var dto = mapper.ToDto(
            item,
            new Dictionary<long, string> { [1] = "generation" },
            new Dictionary<string, BookmarkIconAssetRecord>(),
            new Dictionary<string, SecretIconAssetRecord> { ["secret-icon"] = secretIconAsset.Asset });

        SyncRemoteObjectValidator.Validate(dto);
        Assert.True(dto.IsSecret);
        Assert.Null(dto.Title);
        Assert.Null(dto.Url);
        Assert.Null(dto.IconAssetRef);
        Assert.Equal("secret-icon", dto.SecretIconAssetRef?.AssetId);
        Assert.NotNull(dto.EncryptedPayload);
        Assert.NotNull(dto.EncryptionNonce);
        Assert.Equal("generation", dto.CryptoProfileSecretGenerationId);
    }

    [Fact]
    public void ToDto_maps_assets_profile_and_reset_to_valid_remote_objects()
    {
        var mapper = CreateMapper();

        var iconAsset = mapper.ToDto(CreateIconAsset("icon"));
        var secretIconAsset = mapper.ToDto(CreateSecretIconAsset("secret-icon", "generation"));
        var cryptoProfile = mapper.ToDto(CreateProfile("generation"));
        var resetEvent = mapper.ToDto(CreateResetEvent("generation"));

        SyncRemoteObjectValidator.Validate(iconAsset);
        SyncRemoteObjectValidator.Validate(secretIconAsset);
        SyncRemoteObjectValidator.Validate(cryptoProfile);
        SyncRemoteObjectValidator.Validate(resetEvent);
        Assert.StartsWith("sha256:", iconAsset.ContentHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", secretIconAsset.ContentHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", cryptoProfile.ContentHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", resetEvent.ContentHash, StringComparison.Ordinal);
    }

    private static SyncRemoteDtoMapper CreateMapper()
    {
        return new SyncRemoteDtoMapper(
            new SystemTextSyncJsonSerializer(),
            new Sha256SyncContentHasher());
    }

    private static SyncIconAssetSnapshotRecord CreateIconAsset(string id)
    {
        return new SyncIconAssetSnapshotRecord(
            new BookmarkIconAssetRecord(
                id,
                "sha256",
                "source-hash",
                SourceSizeBytes: 3,
                ProcessedMimeType: "image/png",
                ProcessedWidth: 32,
                ProcessedHeight: 32,
                ProcessedBytes: new byte[] { 1, 2, 3 },
                CreatedAtUtc: Now),
            CreateMetadata());
    }

    private static SyncSecretIconAssetSnapshotRecord CreateSecretIconAsset(
        string id,
        string secretGenerationId)
    {
        return new SyncSecretIconAssetSnapshotRecord(
            new SecretIconAssetRecord(
                id,
                "sha256",
                "secret-source-hash",
                SourceSizeBytes: 3,
                ProcessedMimeType: "image/png",
                ProcessedWidth: 32,
                ProcessedHeight: 32,
                EncryptedProcessedBytes: new EncryptedSecretIconPayloadRecord(
                    Payload: new byte[] { 1, 2, 3 },
                    Nonce: new byte[] { 4, 5, 6 },
                    PayloadFormatVersion: 1),
                SecretGenerationId: secretGenerationId,
                CreatedAtUtc: Now),
            CreateMetadata());
    }

    private static SyncCryptoProfileSnapshotRecord CreateProfile(string secretGenerationId)
    {
        return new SyncCryptoProfileSnapshotRecord(
            new CryptoProfileRecord(
                Id: 1,
                ProfileVersion: 1,
                KdfName: "PBKDF2",
                KdfHashAlgorithm: "SHA256",
                KdfIterations: 1000,
                KdfSalt: new byte[] { 1 },
                KekLengthBytes: 32,
                DataKeyAlgorithm: "AES",
                WrappedDataKey: new byte[] { 2 },
                WrappedDataKeyNonce: new byte[] { 3 },
                EncryptionAlgorithm: "AES-GCM",
                PayloadFormat: "v1",
                PasswordCheckPayload: new byte[] { 4 },
                PasswordCheckNonce: new byte[] { 5 },
                CreatedAtUtc: Now,
                UpdatedAtUtc: Now,
                SecretGenerationId: secretGenerationId),
            CreateMetadata());
    }

    private static SecretResetEventRecord CreateResetEvent(string secretGenerationId)
    {
        return new SecretResetEventRecord(
            secretGenerationId,
            secretGenerationId,
            Now,
            "device",
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null);
    }

    private static SyncItemSnapshotRecord CreateNormalItem(
        string id,
        string? iconAssetId)
    {
        return new SyncItemSnapshotRecord(
            new BookmarkItemRecord(
                id,
                ParentId: null,
                Kind: BookmarkItemKind.Bookmark,
                SortOrder: 1000,
                Title: "Title",
                Url: "https://example.com/",
                IsSecret: false,
                EncryptedPayload: null,
                SecretGenerationId: null,
                Metadata: new BookmarkItemMetadata(
                    Now,
                    Now,
                    DeletedAtUtc: null,
                    Revision: 1,
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    ContentHash: null,
                    ModifiedDeviceId: "device"),
                iconAssetId),
            CreateMetadata());
    }

    private static SyncItemSnapshotRecord CreateSecretItem(
        string id,
        long cryptoProfileId,
        string? secretIconAssetId)
    {
        return new SyncItemSnapshotRecord(
            new BookmarkItemRecord(
                id,
                ParentId: null,
                Kind: BookmarkItemKind.Bookmark,
                SortOrder: 1000,
                Title: null,
                Url: null,
                IsSecret: true,
                EncryptedPayload: new EncryptedBookmarkPayloadRecord(
                    Payload: new byte[] { 1, 2, 3 },
                    Nonce: new byte[] { 4, 5, 6 },
                    CryptoProfileId: cryptoProfileId),
                SecretGenerationId: "generation",
                Metadata: new BookmarkItemMetadata(
                    Now,
                    Now,
                    DeletedAtUtc: null,
                    Revision: 1,
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    ContentHash: null,
                    ModifiedDeviceId: "device"),
                IconAssetId: null,
                SecretIconAssetId: secretIconAssetId),
            CreateMetadata());
    }

    private static SyncObjectMetadata CreateMetadata()
    {
        return new SyncObjectMetadata(
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ContentHash: null,
            ModifiedDeviceId: "device");
    }

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
