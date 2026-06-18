using System.Text.Json.Serialization;

namespace Stranichnik.Sync.Remote;

public sealed record SyncAssetReferenceDto(
    [property: JsonPropertyName("assetId")] string AssetId,
    [property: JsonPropertyName("sourceHashAlgorithm")] string? SourceHashAlgorithm,
    [property: JsonPropertyName("sourceHash")] string? SourceHash);
