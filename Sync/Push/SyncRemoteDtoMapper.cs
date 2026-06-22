using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;

namespace Stranichnik.Sync.Push;

public sealed class SyncRemoteDtoMapper
{
    private readonly ISyncJsonSerializer _serializer;
    private readonly ISyncContentHasher _contentHasher;

    public SyncRemoteDtoMapper(
        ISyncJsonSerializer serializer,
        ISyncContentHasher contentHasher)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(contentHasher);

        _serializer = serializer;
        _contentHasher = contentHasher;
    }

    public SyncItemDto ToDto(
        SyncItemSnapshotRecord item,
        IReadOnlyDictionary<long, string> secretGenerationByProfileId,
        IReadOnlyDictionary<string, BookmarkIconAssetRecord> iconAssetsById,
        IReadOnlyDictionary<string, SecretIconAssetRecord> secretIconAssetsById)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(secretGenerationByProfileId);
        ArgumentNullException.ThrowIfNull(iconAssetsById);
        ArgumentNullException.ThrowIfNull(secretIconAssetsById);

        var itemRecord = item.Item;
        var dto = itemRecord.IsSecret
            ? ToSecretItemDto(itemRecord, secretGenerationByProfileId, secretIconAssetsById)
            : ToNormalItemDto(itemRecord, iconAssetsById);

        return dto with
        {
            ContentHash = ComputeContentHashWithPlaceholder(dto)
        };
    }

    public SyncIconAssetDto ToDto(SyncIconAssetSnapshotRecord iconAsset)
    {
        ArgumentNullException.ThrowIfNull(iconAsset);

        var asset = iconAsset.Asset;
        var dto = new SyncIconAssetDto(
            SyncRemoteObjectConstants.IconAssetSchema,
            SyncRemoteObjectConstants.FormatVersion,
            asset.Id,
            asset.SourceHashAlgorithm,
            asset.SourceHash,
            asset.SourceSizeBytes,
            asset.ProcessedMimeType,
            asset.ProcessedWidth,
            asset.ProcessedHeight,
            Convert.ToBase64String(asset.ProcessedBytes.Span),
            asset.CreatedAtUtc,
            iconAsset.SyncMetadata.LastSyncedAtUtc ?? asset.CreatedAtUtc,
            iconAsset.SyncMetadata.ModifiedDeviceId,
            SyncRemoteObjectConstants.ContentHashPlaceholder);

        return dto with
        {
            ContentHash = ComputeContentHashWithPlaceholder(dto)
        };
    }

    public SyncSecretIconAssetDto ToDto(SyncSecretIconAssetSnapshotRecord secretIconAsset)
    {
        ArgumentNullException.ThrowIfNull(secretIconAsset);

        var asset = secretIconAsset.Asset;
        var dto = new SyncSecretIconAssetDto(
            SyncRemoteObjectConstants.SecretIconAssetSchema,
            SyncRemoteObjectConstants.FormatVersion,
            asset.Id,
            asset.SourceHashAlgorithm,
            asset.SourceHash,
            asset.SourceSizeBytes,
            asset.ProcessedMimeType,
            asset.ProcessedWidth,
            asset.ProcessedHeight,
            Convert.ToBase64String(asset.EncryptedProcessedBytes.Payload.Span),
            Convert.ToBase64String(asset.EncryptedProcessedBytes.Nonce.Span),
            asset.EncryptedProcessedBytes.PayloadFormatVersion,
            asset.SecretGenerationId,
            asset.CreatedAtUtc,
            secretIconAsset.SyncMetadata.LastSyncedAtUtc ?? asset.CreatedAtUtc,
            secretIconAsset.SyncMetadata.ModifiedDeviceId,
            SyncRemoteObjectConstants.ContentHashPlaceholder);

        return dto with
        {
            ContentHash = ComputeContentHashWithPlaceholder(dto)
        };
    }

    public SyncCryptoProfileDto ToDto(SyncCryptoProfileSnapshotRecord cryptoProfile)
    {
        ArgumentNullException.ThrowIfNull(cryptoProfile);

        var profile = cryptoProfile.Profile;
        var dto = new SyncCryptoProfileDto(
            SyncRemoteObjectConstants.CryptoProfileSchema,
            SyncRemoteObjectConstants.FormatVersion,
            profile.SecretGenerationId,
            profile.ProfileVersion,
            profile.KdfName,
            profile.KdfIterations,
            Convert.ToBase64String(profile.KdfSalt.Span),
            Convert.ToBase64String(profile.WrappedDataKey.Span),
            Convert.ToBase64String(profile.WrappedDataKeyNonce.Span),
            Convert.ToBase64String(profile.PasswordCheckPayload.Span),
            Convert.ToBase64String(profile.PasswordCheckNonce.Span),
            profile.CreatedAtUtc,
            profile.UpdatedAtUtc,
            cryptoProfile.SyncMetadata.ModifiedDeviceId,
            SyncRemoteObjectConstants.ContentHashPlaceholder);

        return dto with
        {
            ContentHash = ComputeContentHashWithPlaceholder(dto)
        };
    }

    public SyncSecretResetEventDto ToDto(SecretResetEventRecord resetEvent)
    {
        ArgumentNullException.ThrowIfNull(resetEvent);

        var dto = new SyncSecretResetEventDto(
            SyncRemoteObjectConstants.SecretResetEventSchema,
            SyncRemoteObjectConstants.FormatVersion,
            resetEvent.SecretGenerationId,
            resetEvent.ResetAtUtc,
            resetEvent.ResetDeviceId,
            ResetEventFormatVersion: 1,
            CreatedAtUtc: resetEvent.ResetAtUtc,
            UpdatedAtUtc: resetEvent.ResetAtUtc,
            ModifiedDeviceId: resetEvent.ResetDeviceId,
            ContentHash: SyncRemoteObjectConstants.ContentHashPlaceholder);

        return dto with
        {
            ContentHash = ComputeContentHashWithPlaceholder(dto)
        };
    }

    public static IReadOnlyDictionary<long, string> CreateSecretGenerationMap(
        IReadOnlyList<SyncCryptoProfileSnapshotRecord> cryptoProfiles)
    {
        ArgumentNullException.ThrowIfNull(cryptoProfiles);

        return cryptoProfiles.ToDictionary(
            profile => profile.Profile.Id,
            profile => profile.Profile.SecretGenerationId);
    }

    public static IReadOnlyDictionary<string, BookmarkIconAssetRecord> CreateIconAssetMap(
        IReadOnlyList<SyncIconAssetSnapshotRecord> iconAssets)
    {
        ArgumentNullException.ThrowIfNull(iconAssets);

        return iconAssets.ToDictionary(asset => asset.Asset.Id, asset => asset.Asset);
    }

    public static IReadOnlyDictionary<string, SecretIconAssetRecord> CreateSecretIconAssetMap(
        IReadOnlyList<SyncSecretIconAssetSnapshotRecord> secretIconAssets)
    {
        ArgumentNullException.ThrowIfNull(secretIconAssets);

        return secretIconAssets.ToDictionary(asset => asset.Asset.Id, asset => asset.Asset);
    }

    private static SyncItemDto ToNormalItemDto(
        BookmarkItemRecord item,
        IReadOnlyDictionary<string, BookmarkIconAssetRecord> iconAssetsById)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            item.Id,
            item.ParentId,
            ToRemoteItemKind(item.Kind),
            item.SortOrder,
            item.Title,
            item.Url,
            IsSecret: false,
            IconAssetRef: CreateIconAssetReference(item.IconAssetId, iconAssetsById),
            SecretIconAssetRef: null,
            EncryptedPayload: null,
            EncryptionNonce: null,
            CryptoProfileSecretGenerationId: null,
            SecretPayloadFormatVersion: null,
            CreatedAtUtc: item.Metadata.CreatedAtUtc,
            UpdatedAtUtc: item.Metadata.UpdatedAtUtc,
            DeletedAtUtc: item.Metadata.DeletedAtUtc,
            Revision: item.Metadata.Revision,
            ModifiedDeviceId: item.Metadata.ModifiedDeviceId,
            ContentHash: SyncRemoteObjectConstants.ContentHashPlaceholder);
    }

    private static SyncItemDto ToSecretItemDto(
        BookmarkItemRecord item,
        IReadOnlyDictionary<long, string> secretGenerationByProfileId,
        IReadOnlyDictionary<string, SecretIconAssetRecord> secretIconAssetsById)
    {
        var encryptedPayload = item.EncryptedPayload
            ?? throw new InvalidOperationException("Secret item payload is missing.");
        var secretGenerationId = item.SecretGenerationId;
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new InvalidOperationException("Secret item generation is missing.");

        if (!secretGenerationByProfileId.TryGetValue(encryptedPayload.CryptoProfileId, out var profileSecretGenerationId))
            throw new InvalidOperationException("Secret item crypto profile is missing.");
        if (!string.Equals(profileSecretGenerationId, secretGenerationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret item generation does not match crypto profile generation.");

        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            item.Id,
            item.ParentId,
            ToRemoteItemKind(item.Kind),
            item.SortOrder,
            Title: null,
            Url: null,
            IsSecret: true,
            IconAssetRef: null,
            SecretIconAssetRef: CreateSecretIconAssetReference(item.SecretIconAssetId, secretIconAssetsById),
            EncryptedPayload: Convert.ToBase64String(encryptedPayload.Payload.Span),
            EncryptionNonce: Convert.ToBase64String(encryptedPayload.Nonce.Span),
            CryptoProfileSecretGenerationId: secretGenerationId,
            SecretPayloadFormatVersion: encryptedPayload.PayloadFormatVersion,
            CreatedAtUtc: item.Metadata.CreatedAtUtc,
            UpdatedAtUtc: item.Metadata.UpdatedAtUtc,
            DeletedAtUtc: item.Metadata.DeletedAtUtc,
            Revision: item.Metadata.Revision,
            ModifiedDeviceId: item.Metadata.ModifiedDeviceId,
            ContentHash: SyncRemoteObjectConstants.ContentHashPlaceholder);
    }

    private static SyncAssetReferenceDto? CreateIconAssetReference(
        string? iconAssetId,
        IReadOnlyDictionary<string, BookmarkIconAssetRecord> iconAssetsById)
    {
        if (iconAssetId is null)
            return null;

        if (!iconAssetsById.TryGetValue(iconAssetId, out var iconAsset))
            throw new InvalidOperationException("Item icon asset is missing.");

        return new SyncAssetReferenceDto(
            iconAsset.Id,
            iconAsset.SourceHashAlgorithm,
            iconAsset.SourceHash);
    }

    private static SyncAssetReferenceDto? CreateSecretIconAssetReference(
        string? secretIconAssetId,
        IReadOnlyDictionary<string, SecretIconAssetRecord> secretIconAssetsById)
    {
        if (secretIconAssetId is null)
            return null;

        if (!secretIconAssetsById.TryGetValue(secretIconAssetId, out var secretIconAsset))
            throw new InvalidOperationException("Item secret icon asset is missing.");

        return new SyncAssetReferenceDto(
            secretIconAsset.Id,
            secretIconAsset.SourceHashAlgorithm,
            secretIconAsset.SourceHash);
    }

    private string ComputeContentHashWithPlaceholder<T>(T dto)
    {
        // The DTO hash is calculated while contentHash has a stable placeholder value.
        // This avoids recursive "hash includes itself" semantics.
        var canonicalJson = _serializer.Serialize(dto);
        return _contentHasher.ComputeHash(canonicalJson);
    }

    private static string ToRemoteItemKind(BookmarkItemKind kind)
    {
        return kind switch
        {
            BookmarkItemKind.Folder => SyncRemoteObjectConstants.FolderKind,
            BookmarkItemKind.Bookmark => SyncRemoteObjectConstants.BookmarkKind,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported bookmark item kind.")
        };
    }
}
