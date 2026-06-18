using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncSecretIconAssetDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourceHashAlgorithm")] string SourceHashAlgorithm,
    [property: JsonPropertyName("sourceHash")] string SourceHash,
    [property: JsonPropertyName("sourceSizeBytes")] long SourceSizeBytes,
    [property: JsonPropertyName("processedMimeType")] string ProcessedMimeType,
    [property: JsonPropertyName("processedWidth")] int ProcessedWidth,
    [property: JsonPropertyName("processedHeight")] int ProcessedHeight,
    [property: JsonPropertyName("encryptedProcessedBytes")] string EncryptedProcessedBytes,
    [property: JsonPropertyName("encryptionNonce")] string EncryptionNonce,
    [property: JsonPropertyName("payloadFormatVersion")] int PayloadFormatVersion,
    [property: JsonPropertyName("secretGenerationId")] string SecretGenerationId,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("modifiedDeviceId")] string ModifiedDeviceId,
    [property: JsonPropertyName("contentHash")] string ContentHash);
