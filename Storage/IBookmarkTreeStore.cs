namespace Stranichnik.Storage;

public interface IBookmarkTreeStore
{
    BookmarkTreeSnapshot Load();

    BookmarkItemRecord AddBookmarkToFolderStart(
        string? parentId,
        string title,
        string url);

    BookmarkItemRecord AddFolderToFolderStart(
        string? parentId,
        string title);

    BookmarkItemRecord EditBookmark(
        string bookmarkId,
        string title,
        string url);

    BookmarkItemRecord EditFolder(
        string folderId,
        string title);

    void DeleteItem(string itemId);

    bool CanMoveToFolderStart(
        string itemId,
        string? targetParentId);

    BookmarkItemRecord MoveToFolderStart(
        string itemId,
        string? targetParentId);
}
