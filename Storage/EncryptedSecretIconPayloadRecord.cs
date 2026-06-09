using System;

namespace Stranichnik.Storage;

public sealed record EncryptedSecretIconPayloadRecord(
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> Nonce,
    int PayloadFormatVersion);
