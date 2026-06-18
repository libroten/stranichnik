using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncDeviceInfoDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("appVersion")] string? AppVersion,
    [property: JsonPropertyName("lastSeenAtUtc")] DateTimeOffset LastSeenAtUtc,
    [property: JsonPropertyName("contentHash")] string ContentHash);
