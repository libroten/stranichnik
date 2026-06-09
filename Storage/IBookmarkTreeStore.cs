namespace Stranichnik.Storage;

public interface IBookmarkTreeStore
{
    BookmarkTreeSnapshot Load();

    BookmarkIconAssetRecord? GetIconAsset(string iconAssetId);

    BookmarkIconAssetRecord? GetIconAssetBySourceHash(
        string sourceHashAlgorithm,
        string sourceHash);

    BookmarkIconAssetRecord GetOrCreateIconAsset(BookmarkIconAssetRecord iconAsset);

    SecretIconAssetRecord? GetSecretIconAsset(string secretIconAssetId);

    SecretIconAssetRecord? GetSecretIconAssetBySourceHash(
        string sourceHashAlgorithm,
        string sourceHash);

    SecretIconAssetRecord GetOrCreateSecretIconAsset(SecretIconAssetRecord secretIconAsset);

    BookmarkItemRecord AddBookmarkToFolderStart(
        string? parentId,
        string title,
        string url);

    BookmarkItemRecord AddSecretBookmarkToFolderStart(
        string? parentId,
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload);

    BookmarkItemRecord AddFolderToFolderStart(
        string? parentId,
        string title);

    BookmarkItemRecord EditBookmark(
        string bookmarkId,
        string title,
        string url);

    BookmarkItemRecord EditBookmarkAsSecret(
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload);

    BookmarkItemRecord EditSecretBookmarkAsPlaintext(
        string bookmarkId,
        string title,
        string url);

    BookmarkItemRecord EditFolder(
        string folderId,
        string title);

    BookmarkItemRecord SetItemIconAsset(
        string itemId,
        string? iconAssetId);

    BookmarkItemRecord SetItemSecretIconAsset(
        string itemId,
        string? secretIconAssetId);

    void DeleteItem(string itemId);

    bool CanMoveToFolderStart(
        string itemId,
        string? targetParentId);

    BookmarkItemRecord MoveToFolderStart(
        string itemId,
        string? targetParentId);
}
