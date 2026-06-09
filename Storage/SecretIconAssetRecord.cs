using System;

namespace Stranichnik.Storage;

public sealed record SecretIconAssetRecord(
    string Id,
    string SourceHashAlgorithm,
    string SourceHash,
    long SourceSizeBytes,
    string ProcessedMimeType,
    int ProcessedWidth,
    int ProcessedHeight,
    EncryptedSecretIconPayloadRecord EncryptedProcessedBytes,
    string SecretGenerationId,
    DateTimeOffset CreatedAtUtc);
