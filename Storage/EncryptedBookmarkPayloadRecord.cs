using System;

namespace Stranichnik.Storage;

public sealed record EncryptedBookmarkPayloadRecord(
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> Nonce,
    long CryptoProfileId);
