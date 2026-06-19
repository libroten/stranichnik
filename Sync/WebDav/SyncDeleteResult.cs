namespace Stranichnik.Sync.WebDav;

public sealed record SyncDeleteResult(
    SyncDeleteStatus Status,
    string? ETag)
{
    public static SyncDeleteResult DeletedOrMissing()
    {
        return new SyncDeleteResult(SyncDeleteStatus.DeletedOrMissing, null);
    }

    public static SyncDeleteResult PreconditionFailed(string? currentEtag)
    {
        return new SyncDeleteResult(SyncDeleteStatus.PreconditionFailed, currentEtag);
    }
}
