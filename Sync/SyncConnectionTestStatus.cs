namespace Stranichnik.Sync;

public enum SyncConnectionTestStatus
{
    Succeeded,
    MissingWebDavUrl,
    InvalidWebDavUrl,
    MissingUsername,
    MissingCredentials,
    WrongCredentials,
    RemoteUnavailable
}
