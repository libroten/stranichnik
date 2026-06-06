namespace Stranichnik.Security;

public sealed record SecretProfileSetupResult(
    bool WasCreated,
    CryptoProfileRecord? Profile)
{
    public static SecretProfileSetupResult Created(CryptoProfileRecord profile)
    {
        return new(true, profile);
    }

    public static SecretProfileSetupResult AlreadyConfigured()
    {
        return new(false, Profile: null);
    }
}
