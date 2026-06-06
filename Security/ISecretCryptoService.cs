using System;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public interface ISecretCryptoService
{
    CryptoProfileCreationResult CreateProfile(
        string masterPassword,
        DateTimeOffset nowUtc);

    SecretUnlockResult Unlock(
        CryptoProfileRecord profile,
        string masterPassword);

    EncryptedSecretPayload EncryptBookmarkPayload(
        SecretBookmarkPayloadV1 payload,
        RuntimeSecretKey dataKey,
        long cryptoProfileId,
        string itemId);

    SecretBookmarkPayloadV1 DecryptBookmarkPayload(
        EncryptedBookmarkPayloadRecord encryptedPayload,
        RuntimeSecretKey dataKey,
        string itemId);

    CryptoProfileRecord ChangeMasterPassword(
        CryptoProfileRecord profile,
        RuntimeSecretKey dataKey,
        string newMasterPassword,
        DateTimeOffset nowUtc);
}
