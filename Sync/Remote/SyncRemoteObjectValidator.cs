using System;

namespace Stranichnik.Sync.Remote;

public static class SyncRemoteObjectValidator
{
    public static void Validate(SyncManifestDto manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ValidateCommon(
            manifest.Schema,
            SyncRemoteObjectConstants.ManifestSchema,
            manifest.FormatVersion,
            manifest.RepositoryId);
        RequireValue(manifest.CreatedByDeviceId, nameof(manifest.CreatedByDeviceId));

        if (manifest.MinimumAppSyncVersion < 1)
            throw new SyncRemoteObjectValidationException("Manifest minimum sync version must be positive.");
    }

    public static void Validate(SyncItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ValidateCommon(
            item.Schema,
            SyncRemoteObjectConstants.ItemSchema,
            item.FormatVersion,
            item.Id);
        RequireValue(item.Kind, nameof(item.Kind));
        RequireValue(item.ModifiedDeviceId, nameof(item.ModifiedDeviceId));
        RequireValue(item.ContentHash, nameof(item.ContentHash));

        if (item.Kind is not SyncRemoteObjectConstants.FolderKind and not SyncRemoteObjectConstants.BookmarkKind)
            throw new SyncRemoteObjectValidationException("Item kind is not supported.");

        if (item.Revision < 0)
            throw new SyncRemoteObjectValidationException("Item revision cannot be negative.");

        if (item.IsSecret)
            ValidateSecretItem(item);
        else
            ValidateNormalItem(item);
    }

    public static void Validate(SyncIconAssetDto iconAsset)
    {
        ArgumentNullException.ThrowIfNull(iconAsset);

        ValidateCommon(
            iconAsset.Schema,
            SyncRemoteObjectConstants.IconAssetSchema,
            iconAsset.FormatVersion,
            iconAsset.Id);
        RequireValue(iconAsset.SourceHashAlgorithm, nameof(iconAsset.SourceHashAlgorithm));
        RequireValue(iconAsset.SourceHash, nameof(iconAsset.SourceHash));
        RequireValue(iconAsset.ProcessedMimeType, nameof(iconAsset.ProcessedMimeType));
        RequireBase64Value(iconAsset.ProcessedBytes, nameof(iconAsset.ProcessedBytes));
        RequireValue(iconAsset.ModifiedDeviceId, nameof(iconAsset.ModifiedDeviceId));
        RequireValue(iconAsset.ContentHash, nameof(iconAsset.ContentHash));
        ValidateDimensions(iconAsset.SourceSizeBytes, iconAsset.ProcessedWidth, iconAsset.ProcessedHeight);
    }

    public static void Validate(SyncSecretIconAssetDto secretIconAsset)
    {
        ArgumentNullException.ThrowIfNull(secretIconAsset);

        ValidateCommon(
            secretIconAsset.Schema,
            SyncRemoteObjectConstants.SecretIconAssetSchema,
            secretIconAsset.FormatVersion,
            secretIconAsset.Id);
        RequireValue(secretIconAsset.SourceHashAlgorithm, nameof(secretIconAsset.SourceHashAlgorithm));
        RequireValue(secretIconAsset.SourceHash, nameof(secretIconAsset.SourceHash));
        RequireValue(secretIconAsset.ProcessedMimeType, nameof(secretIconAsset.ProcessedMimeType));
        RequireBase64Value(secretIconAsset.EncryptedProcessedBytes, nameof(secretIconAsset.EncryptedProcessedBytes));
        RequireBase64Value(secretIconAsset.EncryptionNonce, nameof(secretIconAsset.EncryptionNonce));
        RequireValue(secretIconAsset.SecretGenerationId, nameof(secretIconAsset.SecretGenerationId));
        RequireValue(secretIconAsset.ModifiedDeviceId, nameof(secretIconAsset.ModifiedDeviceId));
        RequireValue(secretIconAsset.ContentHash, nameof(secretIconAsset.ContentHash));
        ValidateDimensions(secretIconAsset.SourceSizeBytes, secretIconAsset.ProcessedWidth, secretIconAsset.ProcessedHeight);

        if (secretIconAsset.PayloadFormatVersion < 1)
            throw new SyncRemoteObjectValidationException("Secret icon payload format version must be positive.");
    }

    public static void Validate(SyncCryptoProfileDto cryptoProfile)
    {
        ArgumentNullException.ThrowIfNull(cryptoProfile);

        ValidateCommon(
            cryptoProfile.Schema,
            SyncRemoteObjectConstants.CryptoProfileSchema,
            cryptoProfile.FormatVersion,
            cryptoProfile.SecretGenerationId);
        RequireValue(cryptoProfile.KdfAlgorithm, nameof(cryptoProfile.KdfAlgorithm));
        RequireBase64Value(cryptoProfile.Salt, nameof(cryptoProfile.Salt));
        RequireBase64Value(cryptoProfile.EncryptedDataKey, nameof(cryptoProfile.EncryptedDataKey));
        RequireBase64Value(cryptoProfile.DataKeyNonce, nameof(cryptoProfile.DataKeyNonce));
        RequireBase64Value(cryptoProfile.PasswordCheckPayload, nameof(cryptoProfile.PasswordCheckPayload));
        RequireBase64Value(cryptoProfile.PasswordCheckNonce, nameof(cryptoProfile.PasswordCheckNonce));
        RequireValue(cryptoProfile.ModifiedDeviceId, nameof(cryptoProfile.ModifiedDeviceId));
        RequireValue(cryptoProfile.ContentHash, nameof(cryptoProfile.ContentHash));

        if (cryptoProfile.ProfileVersion < 1)
            throw new SyncRemoteObjectValidationException("Crypto profile version must be positive.");

        if (cryptoProfile.KdfIterations < 1)
            throw new SyncRemoteObjectValidationException("Crypto profile KDF iterations must be positive.");
    }

    public static void Validate(SyncSecretResetEventDto resetEvent)
    {
        ArgumentNullException.ThrowIfNull(resetEvent);

        ValidateCommon(
            resetEvent.Schema,
            SyncRemoteObjectConstants.SecretResetEventSchema,
            resetEvent.FormatVersion,
            resetEvent.SecretGenerationId);
        RequireValue(resetEvent.ResetDeviceId, nameof(resetEvent.ResetDeviceId));
        RequireValue(resetEvent.ModifiedDeviceId, nameof(resetEvent.ModifiedDeviceId));
        RequireValue(resetEvent.ContentHash, nameof(resetEvent.ContentHash));

        if (resetEvent.ResetEventFormatVersion < 1)
            throw new SyncRemoteObjectValidationException("Secret reset event format version must be positive.");
    }

    public static void Validate(SyncDeviceInfoDto deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        ValidateCommon(
            deviceInfo.Schema,
            SyncRemoteObjectConstants.DeviceSchema,
            deviceInfo.FormatVersion,
            deviceInfo.DeviceId);
        RequireValue(deviceInfo.ContentHash, nameof(deviceInfo.ContentHash));
    }

    private static void ValidateCommon(
        string schema,
        string expectedSchema,
        int formatVersion,
        string id)
    {
        if (!string.Equals(schema, expectedSchema, StringComparison.Ordinal))
            throw new SyncRemoteObjectValidationException("Remote object schema is not supported.");

        if (formatVersion != SyncRemoteObjectConstants.FormatVersion)
            throw new SyncRemoteObjectValidationException("Remote object format version is not supported.");

        RequireValue(id, nameof(id));
    }

    private static void ValidateNormalItem(SyncItemDto item)
    {
        ValidateAssetReference(item.IconAssetRef);

        if (item.SecretIconAssetRef is not null)
            throw new SyncRemoteObjectValidationException("Normal item cannot reference a secret icon asset.");

        if (item.EncryptedPayload is not null ||
            item.EncryptionNonce is not null ||
            item.CryptoProfileSecretGenerationId is not null ||
            item.SecretPayloadFormatVersion is not null)
        {
            throw new SyncRemoteObjectValidationException("Normal item cannot contain secret payload fields.");
        }

        if (item.Kind == SyncRemoteObjectConstants.BookmarkKind)
        {
            RequireValue(item.Title, nameof(item.Title));
            RequireValue(item.Url, nameof(item.Url));
        }

        if (item.Kind == SyncRemoteObjectConstants.FolderKind)
        {
            RequireValue(item.Title, nameof(item.Title));

            if (item.Url is not null)
                throw new SyncRemoteObjectValidationException("Folder item cannot contain URL.");
        }
    }

    private static void ValidateSecretItem(SyncItemDto item)
    {
        if (item.Kind != SyncRemoteObjectConstants.BookmarkKind)
            throw new SyncRemoteObjectValidationException("Only bookmarks can be secret.");

        if (item.Title is not null || item.Url is not null)
            throw new SyncRemoteObjectValidationException("Secret item cannot contain plaintext title or URL.");

        if (item.IconAssetRef is not null)
            throw new SyncRemoteObjectValidationException("Secret item cannot reference a regular icon asset.");

        ValidateAssetReference(item.SecretIconAssetRef);

        RequireBase64Value(item.EncryptedPayload, nameof(item.EncryptedPayload));
        RequireBase64Value(item.EncryptionNonce, nameof(item.EncryptionNonce));
        RequireValue(item.CryptoProfileSecretGenerationId, nameof(item.CryptoProfileSecretGenerationId));

        if (item.SecretPayloadFormatVersion is null or < 1)
            throw new SyncRemoteObjectValidationException("Secret item payload format version must be positive.");
    }

    private static void ValidateDimensions(long sourceSizeBytes, int processedWidth, int processedHeight)
    {
        if (sourceSizeBytes < 1)
            throw new SyncRemoteObjectValidationException("Icon source size must be positive.");

        if (processedWidth < 1 || processedHeight < 1)
            throw new SyncRemoteObjectValidationException("Icon dimensions must be positive.");
    }

    private static void ValidateAssetReference(SyncAssetReferenceDto? assetReference)
    {
        if (assetReference is null)
            return;

        RequireValue(assetReference.AssetId, nameof(assetReference.AssetId));
    }

    private static void RequireValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SyncRemoteObjectValidationException($"Remote object field is required: {fieldName}.");
    }

    private static void RequireBase64Value(string? value, string fieldName)
    {
        RequireValue(value, fieldName);

        try
        {
            _ = Convert.FromBase64String(value!);
        }
        catch (FormatException exception)
        {
            throw new SyncRemoteObjectValidationException(
                $"Remote object field is not valid base64: {fieldName}.",
                exception);
        }
    }
}
