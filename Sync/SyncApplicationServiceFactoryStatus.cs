namespace Stranichnik.Sync;

public enum SyncApplicationServiceFactoryStatus
{
    Ready,
    MissingWebDavUrl,
    InvalidWebDavUrl,
    MissingUsername,
    MissingCredentials
}
