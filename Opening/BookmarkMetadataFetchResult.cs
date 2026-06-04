namespace Stranichnik.Opening;

public sealed record BookmarkMetadataFetchResult(
    bool IsSuccess,
    BookmarkPageMetadata? Metadata,
    BookmarkMetadataFetchFailureReason? FailureReason)
{
    public static BookmarkMetadataFetchResult Success(BookmarkPageMetadata metadata)
    {
        return new(true, metadata, FailureReason: null);
    }

    public static BookmarkMetadataFetchResult Failure(BookmarkMetadataFetchFailureReason reason)
    {
        return new(false, Metadata: null, reason);
    }
}
