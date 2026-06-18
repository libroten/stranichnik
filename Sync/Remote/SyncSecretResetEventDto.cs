using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncSecretResetEventDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("secretGenerationId")] string SecretGenerationId,
    [property: JsonPropertyName("resetAtUtc")] DateTimeOffset ResetAtUtc,
    [property: JsonPropertyName("resetDeviceId")] string ResetDeviceId,
    [property: JsonPropertyName("resetEventFormatVersion")] int ResetEventFormatVersion,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("modifiedDeviceId")] string ModifiedDeviceId,
    [property: JsonPropertyName("contentHash")] string ContentHash);
