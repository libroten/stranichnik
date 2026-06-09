using System;

namespace Stranichnik.Security;

public sealed record CryptoProfileRecord(
    long Id,
    int ProfileVersion,
    string KdfName,
    string KdfHashAlgorithm,
    int KdfIterations,
    ReadOnlyMemory<byte> KdfSalt,
    int KekLengthBytes,
    string DataKeyAlgorithm,
    ReadOnlyMemory<byte> WrappedDataKey,
    ReadOnlyMemory<byte> WrappedDataKeyNonce,
    string EncryptionAlgorithm,
    string PayloadFormat,
    ReadOnlyMemory<byte> PasswordCheckPayload,
    ReadOnlyMemory<byte> PasswordCheckNonce,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string SecretGenerationId);
