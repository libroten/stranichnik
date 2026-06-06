namespace Stranichnik.Security;

public sealed record SecretMasterPasswordChangeResult(
    bool WasChanged,
    SecretMasterPasswordChangeFailureReason? FailureReason)
{
    public static SecretMasterPasswordChangeResult Changed()
    {
        return new SecretMasterPasswordChangeResult(true, null);
    }

    public static SecretMasterPasswordChangeResult Failed(SecretMasterPasswordChangeFailureReason failureReason)
    {
        return new SecretMasterPasswordChangeResult(false, failureReason);
    }
}
