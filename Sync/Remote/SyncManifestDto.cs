using System;
using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncManifestDto(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("repositoryId")] string RepositoryId,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("createdByDeviceId")] string CreatedByDeviceId,
    [property: JsonPropertyName("minimumAppSyncVersion")] int MinimumAppSyncVersion);
