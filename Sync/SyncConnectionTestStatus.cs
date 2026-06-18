namespace Stranichnik.Sync;

public enum SyncConnectionTestStatus
{
    Succeeded,
    Disabled,
    MissingWebDavUrl,
    InvalidWebDavUrl,
    MissingUsername,
    MissingCredentials,
    WrongCredentials,
    RemoteUnavailable
}
