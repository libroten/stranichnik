using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretBookmarkProjectionService
{
    private readonly ISecretCryptoService _cryptoService;

    public SecretBookmarkProjectionService(ISecretCryptoService cryptoService)
    {
        _cryptoService = cryptoService;
    }

    public SecretProjectionResult Project(
        BookmarkTreeSnapshot storageSnapshot,
        ISecretSessionService session)
    {
        ArgumentNullException.ThrowIfNull(storageSnapshot);
        ArgumentNullException.ThrowIfNull(session);

        var warnings = new List<SecretProjectionWarning>();
        var projectedItems = session.AreSecretsVisible
            ? ProjectWithVisibleSecrets(storageSnapshot.Items, session, warnings)
            : ProjectWithHiddenSecrets(storageSnapshot.Items);

        return new SecretProjectionResult(
            new BookmarkTreeSnapshot(projectedItems),
            warnings);
    }

    private List<BookmarkItemRecord> ProjectWithVisibleSecrets(
        IReadOnlyList<BookmarkItemRecord> items,
        ISecretSessionService session,
        List<SecretProjectionWarning> warnings)
    {
        var dataKey = session.BorrowDataKey();

        if (dataKey is null)
        {
            warnings.Add(new SecretProjectionWarning(SecretProjectionWarningReason.MissingRuntimeKey));
            return ProjectWithHiddenSecrets(items);
        }

        var visibleItems = new List<BookmarkItemRecord>(items.Count);

        foreach (var item in items)
        {
            if (!item.IsSecret)
            {
                visibleItems.Add(item);
                continue;
            }

            if (item.EncryptedPayload is null)
            {
                warnings.Add(new SecretProjectionWarning(SecretProjectionWarningReason.MissingEncryptedPayload));
                continue;
            }

            try
            {
                var payload = _cryptoService.DecryptBookmarkPayload(item.EncryptedPayload, dataKey, item.Id);
                visibleItems.Add(item with
                {
                    Title = payload.Title,
                    Url = payload.Url
                });
            }
            catch (SecretPayloadException)
            {
                warnings.Add(new SecretProjectionWarning(SecretProjectionWarningReason.DecryptionFailed));
            }
        }

        return RemoveFoldersWithoutVisibleChildren(visibleItems, items);
    }

    private static List<BookmarkItemRecord> ProjectWithHiddenSecrets(
        IReadOnlyList<BookmarkItemRecord> items)
    {
        var visibleItems = items
            .Where(item => !item.IsSecret)
            .ToList();

        return RemoveFoldersWithoutVisibleChildren(visibleItems, items);
    }

    private static List<BookmarkItemRecord> RemoveFoldersWithoutVisibleChildren(
        IReadOnlyList<BookmarkItemRecord> visibleItems,
        IReadOnlyList<BookmarkItemRecord> originalItems)
    {
        var originalChildrenByParentId = originalItems
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var visibleChildrenByParentId = visibleItems
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList(), StringComparer.Ordinal);

        var visibleById = visibleItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var folderIds = visibleItems
            .Where(item => item.Kind == BookmarkItemKind.Folder)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        var keepByFolderId = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var folderId in folderIds)
            ShouldKeepFolder(folderId, originalChildrenByParentId, visibleChildrenByParentId, visibleById, keepByFolderId);

        return visibleItems
            .Where(item => item.Kind != BookmarkItemKind.Folder || keepByFolderId.GetValueOrDefault(item.Id))
            .ToList();
    }

    private static bool ShouldKeepFolder(
        string folderId,
        IReadOnlyDictionary<string, int> originalChildrenByParentId,
        IReadOnlyDictionary<string, List<string>> visibleChildrenByParentId,
        IReadOnlyDictionary<string, BookmarkItemRecord> visibleById,
        Dictionary<string, bool> keepByFolderId)
    {
        if (keepByFolderId.TryGetValue(folderId, out var cached))
            return cached;

        if (!originalChildrenByParentId.ContainsKey(folderId))
        {
            keepByFolderId[folderId] = true;
            return true;
        }

        if (!visibleChildrenByParentId.TryGetValue(folderId, out var visibleChildIds))
        {
            keepByFolderId[folderId] = false;
            return false;
        }

        foreach (var childId in visibleChildIds)
        {
            if (!visibleById.TryGetValue(childId, out var child))
                continue;

            if (child.Kind != BookmarkItemKind.Folder ||
                ShouldKeepFolder(
                    child.Id,
                    originalChildrenByParentId,
                    visibleChildrenByParentId,
                    visibleById,
                    keepByFolderId))
            {
                keepByFolderId[folderId] = true;
                return true;
            }
        }

        keepByFolderId[folderId] = false;
        return false;
    }
}
