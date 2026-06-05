using System;

namespace Stranichnik.Storage;

public sealed record BookmarkIconAssetRecord(
    string Id,
    string SourceHashAlgorithm,
    string SourceHash,
    long SourceSizeBytes,
    string ProcessedMimeType,
    int ProcessedWidth,
    int ProcessedHeight,
    ReadOnlyMemory<byte> ProcessedBytes,
    DateTimeOffset CreatedAtUtc);
