namespace Stranichnik.Security;

public sealed record SecretMasterPasswordResetResult(
    bool WasReset,
    int PurgedSecretBookmarkCount,
    SecretMasterPasswordResetFailureReason? FailureReason)
{
    public static SecretMasterPasswordResetResult Reset(int purgedSecretBookmarkCount)
    {
        return new(true, purgedSecretBookmarkCount, null);
    }

    public static SecretMasterPasswordResetResult Failed(SecretMasterPasswordResetFailureReason failureReason)
    {
        return new(false, 0, failureReason);
    }
}

