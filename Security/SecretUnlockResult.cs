namespace Stranichnik.Security;

public sealed record SecretUnlockResult(
    bool IsSuccess,
    RuntimeSecretKey? DataKey,
    SecretCryptoFailureReason? FailureReason)
{
    public static SecretUnlockResult Succeeded(RuntimeSecretKey dataKey)
    {
        return new SecretUnlockResult(true, dataKey, null);
    }

    public static SecretUnlockResult Failed(SecretCryptoFailureReason failureReason)
    {
        return new SecretUnlockResult(false, null, failureReason);
    }
}
