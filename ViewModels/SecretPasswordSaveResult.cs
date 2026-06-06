namespace Stranichnik.ViewModels;

public sealed record SecretPasswordSaveResult(
    bool Succeeded,
    bool WasCreated,
    SecretPasswordSaveFailureReason? FailureReason)
{
    public static SecretPasswordSaveResult Created()
    {
        return new SecretPasswordSaveResult(true, true, null);
    }

    public static SecretPasswordSaveResult Changed()
    {
        return new SecretPasswordSaveResult(true, false, null);
    }

    public static SecretPasswordSaveResult Failed(SecretPasswordSaveFailureReason failureReason)
    {
        return new SecretPasswordSaveResult(false, false, failureReason);
    }
}
