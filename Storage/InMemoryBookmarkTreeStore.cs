using System;
using System.Collections.Generic;
using System.Linq;

namespace Stranichnik.Storage;

public sealed class InMemoryBookmarkTreeStore : IBookmarkTreeStore
{
    private const long SortOrderStep = 1000;

    private readonly List<BookmarkItemRecord> _items;
    private readonly List<BookmarkIconAssetRecord> _iconAssets = [];
    private readonly List<SecretIconAssetRecord> _secretIconAssets = [];
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _modifiedDeviceId;

    public InMemoryBookmarkTreeStore()
        : this(
            [],
            () => Guid.NewGuid().ToString("N"),
            () => DateTimeOffset.UtcNow,
            "local")
    {
    }

    public InMemoryBookmarkTreeStore(
        IEnumerable<BookmarkItemRecord> items,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null,
        string modifiedDeviceId = "local")
    {
        _items = items.ToList();
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _modifiedDeviceId = modifiedDeviceId;
    }

    public BookmarkTreeSnapshot Load()
    {
        return new(_items
            .Where(IsVisible)
            .OrderBy(item => item.ParentId ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(item => item.SortOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList());
    }

    public BookmarkIconAssetRecord? GetIconAsset(string iconAssetId)
    {
        ArgumentNullException.ThrowIfNull(iconAssetId);

        return _iconAssets.FirstOrDefault(iconAsset => iconAsset.Id == iconAssetId);
    }

    public BookmarkIconAssetRecord? GetIconAssetBySourceHash(
        string sourceHashAlgorithm,
        string sourceHash)
    {
        ArgumentNullException.ThrowIfNull(sourceHashAlgorithm);
        ArgumentNullException.ThrowIfNull(sourceHash);

        return _iconAssets.FirstOrDefault(iconAsset =>
            string.Equals(iconAsset.SourceHashAlgorithm, sourceHashAlgorithm, StringComparison.Ordinal)
            && string.Equals(iconAsset.SourceHash, sourceHash, StringComparison.Ordinal));
    }

    public BookmarkIconAssetRecord GetOrCreateIconAsset(BookmarkIconAssetRecord iconAsset)
    {
        ArgumentNullException.ThrowIfNull(iconAsset);

        var existing = GetIconAssetBySourceHash(iconAsset.SourceHashAlgorithm, iconAsset.SourceHash);

        if (existing is not null)
            return existing;

        _iconAssets.Add(iconAsset);
        return iconAsset;
    }

    public SecretIconAssetRecord? GetSecretIconAsset(string secretIconAssetId)
    {
        ArgumentNullException.ThrowIfNull(secretIconAssetId);

        return _secretIconAssets.FirstOrDefault(iconAsset => iconAsset.Id == secretIconAssetId);
    }

    public SecretIconAssetRecord? GetSecretIconAssetBySourceHash(
        string sourceHashAlgorithm,
        string sourceHash)
    {
        ArgumentNullException.ThrowIfNull(sourceHashAlgorithm);
        ArgumentNullException.ThrowIfNull(sourceHash);

        return _secretIconAssets.FirstOrDefault(iconAsset =>
            string.Equals(iconAsset.SourceHashAlgorithm, sourceHashAlgorithm, StringComparison.Ordinal)
            && string.Equals(iconAsset.SourceHash, sourceHash, StringComparison.Ordinal));
    }

    public SecretIconAssetRecord GetOrCreateSecretIconAsset(SecretIconAssetRecord secretIconAsset)
    {
        ArgumentNullException.ThrowIfNull(secretIconAsset);

        var existing = GetSecretIconAssetBySourceHash(
            secretIconAsset.SourceHashAlgorithm,
            secretIconAsset.SourceHash);

        if (existing is not null)
            return existing;

        _secretIconAssets.Add(secretIconAsset);
        return secretIconAsset;
    }

    public BookmarkItemRecord AddBookmarkToFolderStart(
        string? parentId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));
        EnsureParentFolderExists(parentId);

        var now = _clock();
        var bookmark = new BookmarkItemRecord(
            _idFactory(),
            parentId,
            BookmarkItemKind.Bookmark,
            AllocateStartSortOrder(parentId),
            normalizedTitle,
            normalizedUrl,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(now));

        _items.Add(bookmark);

        return bookmark;
    }

    public BookmarkItemRecord AddSecretBookmarkToFolderStart(
        string? parentId,
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        var normalizedBookmarkId = NormalizeRequired(bookmarkId, nameof(bookmarkId));
        ValidateEncryptedPayload(encryptedPayload);
        EnsureParentFolderExists(parentId);

        var now = _clock();
        var bookmark = new BookmarkItemRecord(
            normalizedBookmarkId,
            parentId,
            BookmarkItemKind.Bookmark,
            AllocateStartSortOrder(parentId),
            Title: null,
            Url: null,
            IsSecret: true,
            encryptedPayload,
            CreateMetadata(now));

        _items.Add(bookmark);

        return bookmark;
    }

    public BookmarkItemRecord AddFolderToFolderStart(
        string? parentId,
        string title)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        EnsureParentFolderExists(parentId);

        var now = _clock();
        var folder = new BookmarkItemRecord(
            _idFactory(),
            parentId,
            BookmarkItemKind.Folder,
            AllocateStartSortOrder(parentId),
            normalizedTitle,
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(now));

        _items.Add(folder);

        return folder;
    }

    public BookmarkItemRecord EditBookmark(
        string bookmarkId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));
        var bookmark = GetVisibleItem(bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as bookmarks.");

        if (bookmark.IsSecret)
            throw new InvalidOperationException("Secret bookmark editing is not implemented yet.");

        return Replace(bookmark with
        {
            Title = normalizedTitle,
            Url = normalizedUrl,
            Metadata = Touch(bookmark.Metadata)
        });
    }

    public BookmarkItemRecord EditBookmarkAsSecret(
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        ValidateEncryptedPayload(encryptedPayload);
        var bookmark = GetVisibleItem(bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as secret bookmarks.");

        return Replace(bookmark with
        {
            Title = null,
            Url = null,
            IsSecret = true,
            EncryptedPayload = encryptedPayload,
            IconAssetId = null,
            Metadata = Touch(bookmark.Metadata)
        });
    }

    public BookmarkItemRecord EditSecretBookmarkAsPlaintext(
        string bookmarkId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));
        var bookmark = GetVisibleItem(bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as bookmarks.");

        if (!bookmark.IsSecret)
            throw new InvalidOperationException("Only secret bookmarks can be converted to plaintext bookmarks.");

        return Replace(bookmark with
        {
            Title = normalizedTitle,
            Url = normalizedUrl,
            IsSecret = false,
            EncryptedPayload = null,
            SecretIconAssetId = null,
            IconAssetId = null,
            Metadata = Touch(bookmark.Metadata)
        });
    }

    public BookmarkItemRecord EditFolder(
        string folderId,
        string title)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var folder = GetVisibleItem(folderId);

        if (folder.Kind != BookmarkItemKind.Folder)
            throw new InvalidOperationException("Only folders can be edited as folders.");

        return Replace(folder with
        {
            Title = normalizedTitle,
            Metadata = Touch(folder.Metadata)
        });
    }

    public BookmarkItemRecord SetItemIconAsset(
        string itemId,
        string? iconAssetId)
    {
        var item = GetVisibleItem(itemId);

        if (item.IsSecret)
            throw new InvalidOperationException("Secret bookmarks cannot use plaintext custom icons.");

        if (iconAssetId is not null && GetIconAsset(iconAssetId) is null)
            throw new InvalidOperationException("Icon asset was not found.");

        return Replace(item with
        {
            IconAssetId = iconAssetId,
            SecretIconAssetId = null,
            Metadata = Touch(item.Metadata)
        });
    }

    public BookmarkItemRecord SetItemSecretIconAsset(
        string itemId,
        string? secretIconAssetId)
    {
        var item = GetVisibleItem(itemId);

        if (!item.IsSecret)
            throw new InvalidOperationException("Plaintext items cannot use encrypted custom icons.");

        if (item.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only secret bookmarks can use encrypted custom icons.");

        if (secretIconAssetId is not null && GetSecretIconAsset(secretIconAssetId) is null)
            throw new InvalidOperationException("Secret icon asset was not found.");

        return Replace(item with
        {
            IconAssetId = null,
            SecretIconAssetId = secretIconAssetId,
            Metadata = Touch(item.Metadata)
        });
    }

    public void DeleteItem(string itemId)
    {
        var item = GetVisibleItem(itemId);
        var idsToDelete = item.Kind == BookmarkItemKind.Folder
            ? GetDescendantIds(item.Id).Append(item.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal) { item.Id };

        var now = _clock();

        for (var index = 0; index < _items.Count; index++)
        {
            var current = _items[index];

            if (!idsToDelete.Contains(current.Id) || current.Metadata.DeletedAtUtc is not null)
                continue;

            _items[index] = current with
            {
                Metadata = Touch(current.Metadata, now) with
                {
                    DeletedAtUtc = now
                }
            };
        }
    }

    internal int PurgeSecretBookmarksForMasterPasswordReset()
    {
        var folderIdsToPurge = FindSecretOnlyFolderIdsForMasterPasswordReset();
        var purgedSecretBookmarkCount = _items.Count(item => item is { Kind: BookmarkItemKind.Bookmark, IsSecret: true });

        _items.RemoveAll(item =>
            item is { Kind: BookmarkItemKind.Bookmark, IsSecret: true } ||
            folderIdsToPurge.Contains(item.Id));
        _secretIconAssets.Clear();

        return purgedSecretBookmarkCount;
    }

    public bool CanMoveToFolderStart(
        string itemId,
        string? targetParentId)
    {
        if (!TryGetVisibleItem(itemId, out var item))
            return false;

        if (!TryGetParentFolder(targetParentId, out _))
            return false;

        if (item.ParentId == targetParentId)
            return false;

        if (item.Id == targetParentId)
            return false;

        if (item.Kind == BookmarkItemKind.Folder && IsDescendantOf(targetParentId, item.Id))
            return false;

        return true;
    }

    public BookmarkItemRecord MoveToFolderStart(
        string itemId,
        string? targetParentId)
    {
        var item = GetVisibleItem(itemId);

        if (!CanMoveToFolderStart(itemId, targetParentId))
            throw new InvalidOperationException("Bookmark tree item cannot be moved to the target folder.");

        return Replace(item with
        {
            ParentId = targetParentId,
            SortOrder = AllocateStartSortOrder(targetParentId),
            Metadata = Touch(item.Metadata)
        });
    }

    private static bool IsVisible(BookmarkItemRecord item)
    {
        return item.Metadata.DeletedAtUtc is null;
    }

    private BookmarkItemRecord GetVisibleItem(string itemId)
    {
        return TryGetVisibleItem(itemId, out var item)
            ? item
            : throw new InvalidOperationException("Bookmark tree item was not found.");
    }

    private bool TryGetVisibleItem(string itemId, out BookmarkItemRecord item)
    {
        item = _items.FirstOrDefault(item => item.Id == itemId && IsVisible(item))!;
        return item is not null;
    }

    private void EnsureParentFolderExists(string? parentId)
    {
        if (!TryGetParentFolder(parentId, out _))
            throw new InvalidOperationException("Parent item must be a folder.");
    }

    private bool TryGetParentFolder(string? parentId, out BookmarkItemRecord? parent)
    {
        parent = null;

        if (parentId is null)
            return true;

        if (!TryGetVisibleItem(parentId, out var item) || item.Kind != BookmarkItemKind.Folder)
            return false;

        parent = item;
        return true;
    }

    private long AllocateStartSortOrder(string? parentId)
    {
        var maxSortOrder = _items
            .Where(item => IsVisible(item) && item.ParentId == parentId)
            .Select(item => (long?)item.SortOrder)
            .Max();

        return (maxSortOrder ?? 0) + SortOrderStep;
    }

    private BookmarkItemRecord Replace(BookmarkItemRecord updatedItem)
    {
        var index = _items.FindIndex(item => item.Id == updatedItem.Id);

        if (index < 0)
            throw new InvalidOperationException("Bookmark tree item was not found.");

        _items[index] = updatedItem;

        return updatedItem;
    }

    private IEnumerable<string> GetDescendantIds(string folderId)
    {
        foreach (var child in _items.Where(item => item.ParentId == folderId && IsVisible(item)))
        {
            yield return child.Id;

            if (child.Kind != BookmarkItemKind.Folder)
                continue;

            foreach (var descendantId in GetDescendantIds(child.Id))
                yield return descendantId;
        }
    }

    private HashSet<string> FindSecretOnlyFolderIdsForMasterPasswordReset()
    {
        var liveChildrenByParentId = _items
            .Where(item => IsVisible(item) && item.ParentId is not null)
            .GroupBy(item => item.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var folderIds = _items
            .Where(item => item.Kind == BookmarkItemKind.Folder)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        var visibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        var secretDescendants = new Dictionary<string, bool>(StringComparer.Ordinal);

        return folderIds
            .Where(folderId =>
                HasSecretBookmarkDescendant(folderId, liveChildrenByParentId, secretDescendants) &&
                !IsVisibleWhenSecretsAreHidden(folderId, liveChildrenByParentId, visibility))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasSecretBookmarkDescendant(
        string folderId,
        Dictionary<string, List<BookmarkItemRecord>> childrenByParentId,
        Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(folderId, out var cached))
            return cached;

        if (!childrenByParentId.TryGetValue(folderId, out var children))
        {
            cache[folderId] = false;
            return false;
        }

        var hasSecretBookmark = children.Any(child =>
            child is { Kind: BookmarkItemKind.Bookmark, IsSecret: true } ||
            child.Kind == BookmarkItemKind.Folder &&
            HasSecretBookmarkDescendant(child.Id, childrenByParentId, cache));

        cache[folderId] = hasSecretBookmark;
        return hasSecretBookmark;
    }

    private static bool IsVisibleWhenSecretsAreHidden(
        string folderId,
        Dictionary<string, List<BookmarkItemRecord>> childrenByParentId,
        Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(folderId, out var cached))
            return cached;

        if (!childrenByParentId.TryGetValue(folderId, out var children))
        {
            cache[folderId] = true;
            return true;
        }

        var isVisible = children.Any(child =>
            child is { Kind: BookmarkItemKind.Bookmark, IsSecret: false } ||
            child.Kind == BookmarkItemKind.Folder &&
            IsVisibleWhenSecretsAreHidden(child.Id, childrenByParentId, cache));

        cache[folderId] = isVisible;
        return isVisible;
    }

    private bool IsDescendantOf(string? possibleDescendantId, string folderId)
    {
        var currentId = possibleDescendantId;

        while (currentId is not null)
        {
            if (currentId == folderId)
                return true;

            currentId = _items
                .FirstOrDefault(item => item.Id == currentId && IsVisible(item))
                ?.ParentId;
        }

        return false;
    }

    private BookmarkItemMetadata CreateMetadata(DateTimeOffset now)
    {
        return new(
            now,
            now,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            _modifiedDeviceId);
    }

    private BookmarkItemMetadata Touch(BookmarkItemMetadata metadata)
    {
        return Touch(metadata, _clock());
    }

    private BookmarkItemMetadata Touch(BookmarkItemMetadata metadata, DateTimeOffset now)
    {
        return metadata with
        {
            UpdatedAtUtc = now,
            Revision = metadata.Revision + 1,
            SyncState = BookmarkSyncState.Dirty,
            ModifiedDeviceId = _modifiedDeviceId
        };
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = value.Trim();

        if (normalized.Length == 0)
            throw new ArgumentException("Value cannot be empty.", parameterName);

        return normalized;
    }

    private static void ValidateEncryptedPayload(EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);

        if (encryptedPayload.Payload.IsEmpty ||
            encryptedPayload.Nonce.IsEmpty ||
            encryptedPayload.CryptoProfileId <= 0 ||
            encryptedPayload.PayloadFormatVersion <= 0)
        {
            throw new ArgumentException("Encrypted bookmark payload is invalid.", nameof(encryptedPayload));
        }
    }
}
