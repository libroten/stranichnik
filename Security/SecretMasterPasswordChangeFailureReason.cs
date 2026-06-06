namespace Stranichnik.Security;

public enum SecretMasterPasswordChangeFailureReason
{
    NotConfigured,
    Locked,
    UnsupportedProfile,
    StoreUpdateFailed
}
