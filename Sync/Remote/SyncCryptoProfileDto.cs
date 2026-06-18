using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncCryptoProfileDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("secretGenerationId")] string SecretGenerationId,
    [property: JsonPropertyName("profileVersion")] int ProfileVersion,
    [property: JsonPropertyName("kdfAlgorithm")] string KdfAlgorithm,
    [property: JsonPropertyName("kdfIterations")] int KdfIterations,
    [property: JsonPropertyName("salt")] string Salt,
    [property: JsonPropertyName("encryptedDataKey")] string EncryptedDataKey,
    [property: JsonPropertyName("dataKeyNonce")] string DataKeyNonce,
    [property: JsonPropertyName("passwordCheckPayload")] string PasswordCheckPayload,
    [property: JsonPropertyName("passwordCheckNonce")] string PasswordCheckNonce,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("modifiedDeviceId")] string ModifiedDeviceId,
    [property: JsonPropertyName("contentHash")] string ContentHash);
