namespace Stranichnik.Sync;

public sealed record SyncApplicationServiceFactoryResult(
    SyncApplicationServiceFactoryStatus Status,
    SyncApplicationService? Service)
{
    public static SyncApplicationServiceFactoryResult Ready(SyncApplicationService service)
    {
        return new SyncApplicationServiceFactoryResult(SyncApplicationServiceFactoryStatus.Ready, service);
    }

    public static SyncApplicationServiceFactoryResult MissingWebDavUrl()
    {
        return new SyncApplicationServiceFactoryResult(SyncApplicationServiceFactoryStatus.MissingWebDavUrl, null);
    }

    public static SyncApplicationServiceFactoryResult InvalidWebDavUrl()
    {
        return new SyncApplicationServiceFactoryResult(SyncApplicationServiceFactoryStatus.InvalidWebDavUrl, null);
    }

    public static SyncApplicationServiceFactoryResult MissingUsername()
    {
        return new SyncApplicationServiceFactoryResult(SyncApplicationServiceFactoryStatus.MissingUsername, null);
    }

    public static SyncApplicationServiceFactoryResult MissingCredentials()
    {
        return new SyncApplicationServiceFactoryResult(SyncApplicationServiceFactoryStatus.MissingCredentials, null);
    }
}
