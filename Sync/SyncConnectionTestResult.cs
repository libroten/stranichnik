namespace Stranichnik.Sync;

public sealed record SyncConnectionTestResult(SyncConnectionTestStatus Status)
{
    public static SyncConnectionTestResult Succeeded()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.Succeeded);
    }

    public static SyncConnectionTestResult Disabled()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.Disabled);
    }

    public static SyncConnectionTestResult MissingWebDavUrl()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.MissingWebDavUrl);
    }

    public static SyncConnectionTestResult InvalidWebDavUrl()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.InvalidWebDavUrl);
    }

    public static SyncConnectionTestResult MissingUsername()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.MissingUsername);
    }

    public static SyncConnectionTestResult MissingCredentials()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.MissingCredentials);
    }

    public static SyncConnectionTestResult WrongCredentials()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.WrongCredentials);
    }

    public static SyncConnectionTestResult RemoteUnavailable()
    {
        return new SyncConnectionTestResult(SyncConnectionTestStatus.RemoteUnavailable);
    }
}
