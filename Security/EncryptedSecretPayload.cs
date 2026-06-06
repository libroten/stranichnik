using System;

namespace Stranichnik.Security;

public sealed record EncryptedSecretPayload(
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> Nonce,
    long CryptoProfileId,
    int PayloadFormatVersion);
