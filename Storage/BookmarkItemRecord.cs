namespace Stranichnik.Storage;

public sealed record BookmarkItemRecord(
    string Id,
    string? ParentId,
    BookmarkItemKind Kind,
    long SortOrder,
    string? Title,
    string? Url,
    bool IsSecret,
    EncryptedBookmarkPayloadRecord? EncryptedPayload,
    BookmarkItemMetadata Metadata,
    string? IconAssetId = null);
