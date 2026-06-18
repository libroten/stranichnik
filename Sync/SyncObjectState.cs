namespace Stranichnik.Sync;

public enum SyncObjectState
{
    Clean,
    Dirty,
    Conflict,
    SyncError
}
