namespace Stranichnik.Security;

public sealed record SecretSessionUnlockResult(
    bool WasUnlocked,
    SecretCryptoFailureReason? FailureReason)
{
    public static SecretSessionUnlockResult Unlocked()
    {
        return new SecretSessionUnlockResult(true, null);
    }

    public static SecretSessionUnlockResult Failed(SecretCryptoFailureReason failureReason)
    {
        return new SecretSessionUnlockResult(false, failureReason);
    }

    public static SecretSessionUnlockResult NotConfigured()
    {
        return Failed(SecretCryptoFailureReason.UnsupportedProfile);
    }
}
