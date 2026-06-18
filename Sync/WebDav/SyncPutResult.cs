namespace Stranichnik.Sync.WebDav;

public sealed record SyncPutResult(
    SyncPutStatus Status,
    string? ETag)
{
    public static SyncPutResult CreatedOrUpdated(string? etag)
    {
        return new SyncPutResult(SyncPutStatus.CreatedOrUpdated, etag);
    }

    public static SyncPutResult PreconditionFailed(string? currentEtag)
    {
        return new SyncPutResult(SyncPutStatus.PreconditionFailed, currentEtag);
    }
}
