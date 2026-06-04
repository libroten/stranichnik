namespace Stranichnik.Opening;

public enum BookmarkMetadataFetchFailureReason
{
    InvalidAddress,
    UnsupportedScheme,
    NetworkFailure,
    Timeout,
    NonHtmlResponse,
    MetadataNotFound,
    ResponseTooLarge,
    Cancelled
}
