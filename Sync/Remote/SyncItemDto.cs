using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncItemDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("parentId")] string? ParentId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sortOrder")] long SortOrder,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("isSecret")] bool IsSecret,
    [property: JsonPropertyName("iconAssetRef")] SyncAssetReferenceDto? IconAssetRef,
    [property: JsonPropertyName("secretIconAssetRef")] SyncAssetReferenceDto? SecretIconAssetRef,
    [property: JsonPropertyName("encryptedPayload")] string? EncryptedPayload,
    [property: JsonPropertyName("encryptionNonce")] string? EncryptionNonce,
    [property: JsonPropertyName("cryptoProfileSecretGenerationId")] string? CryptoProfileSecretGenerationId,
    [property: JsonPropertyName("secretPayloadFormatVersion")] int? SecretPayloadFormatVersion,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("deletedAtUtc")] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("modifiedDeviceId")] string ModifiedDeviceId,
    [property: JsonPropertyName("contentHash")] string ContentHash);
