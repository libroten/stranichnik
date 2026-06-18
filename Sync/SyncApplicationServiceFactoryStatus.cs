namespace Stranichnik.Sync;

public enum SyncApplicationServiceFactoryStatus
{
    Ready,
    Disabled,
    MissingWebDavUrl,
    InvalidWebDavUrl,
    MissingUsername,
    MissingCredentials
}
