namespace Stranichnik.Security;

public enum SecretProjectionWarningReason
{
    MissingRuntimeKey,
    MissingEncryptedPayload,
    DecryptionFailed
}
