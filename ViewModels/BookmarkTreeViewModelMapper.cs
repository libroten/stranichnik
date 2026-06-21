using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Stranichnik.Icons;
using Stranichnik.Storage;

namespace Stranichnik.ViewModels;

public static class BookmarkTreeViewModelMapper
{
    private const string RootParentKey = "";
    private const int DefaultExpandedFolderDepth = 2;

    public static ObservableCollection<BookmarkTreeItemViewModel> CreateViewModels(
        BookmarkTreeSnapshot snapshot,
        IReadOnlySet<string>? expandedFolderIds = null,
        BookmarkIconImageCache? iconImageCache = null)
    {
        var liveItems = snapshot.Items
            .Where(item => item.Metadata.DeletedAtUtc is null)
            .ToList();
        var liveItemsById = liveItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var displayParentIds = liveItems.ToDictionary(
            item => item.Id,
            item => GetDisplayParentId(item, liveItemsById),
            StringComparer.Ordinal);
        var itemsByParentId = liveItems
            .GroupBy(item => GetParentKey(displayParentIds[item.Id]), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.SortOrder)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        return CreateChildren(
            parentId: null,
            itemsByParentId,
            expandedFolderIds ?? CreateDefaultExpandedFolderIds(itemsByParentId),
            iconImageCache,
            []);
    }

    private static HashSet<string> CreateDefaultExpandedFolderIds(
        IReadOnlyDictionary<string, List<BookmarkItemRecord>> itemsByParentId)
    {
        var expandedFolderIds = new HashSet<string>(StringComparer.Ordinal);
        AddDefaultExpandedFolderIds(parentId: null, depth: 1, itemsByParentId, expandedFolderIds);

        return expandedFolderIds;
    }

    private static void AddDefaultExpandedFolderIds(
        string? parentId,
        int depth,
        IReadOnlyDictionary<string, List<BookmarkItemRecord>> itemsByParentId,
        HashSet<string> expandedFolderIds)
    {
        if (depth > DefaultExpandedFolderDepth)
            return;

        if (!itemsByParentId.TryGetValue(GetParentKey(parentId), out var childRecords))
            return;

        foreach (var folder in childRecords.Where(item => item.Kind == BookmarkItemKind.Folder))
        {
            if (!expandedFolderIds.Add(folder.Id))
                continue;

            AddDefaultExpandedFolderIds(folder.Id, depth + 1, itemsByParentId, expandedFolderIds);
        }
    }

    private static ObservableCollection<BookmarkTreeItemViewModel> CreateChildren(
        string? parentId,
        IReadOnlyDictionary<string, List<BookmarkItemRecord>> itemsByParentId,
        IReadOnlySet<string> expandedFolderIds,
        BookmarkIconImageCache? iconImageCache,
        HashSet<string> path)
    {
        if (!itemsByParentId.TryGetValue(GetParentKey(parentId), out var childRecords))
            return [];

        var children = new ObservableCollection<BookmarkTreeItemViewModel>();

        foreach (var record in childRecords)
        {
            if (!path.Add(record.Id))
                continue;

            children.Add(CreateViewModel(record, itemsByParentId, expandedFolderIds, iconImageCache, path));
            path.Remove(record.Id);
        }

        return children;
    }

    private static BookmarkTreeItemViewModel CreateViewModel(
        BookmarkItemRecord record,
        IReadOnlyDictionary<string, List<BookmarkItemRecord>> itemsByParentId,
        IReadOnlySet<string> expandedFolderIds,
        BookmarkIconImageCache? iconImageCache,
        HashSet<string> path)
    {
        var iconImage = GetIconImage(record, iconImageCache);

        return record.Kind switch
        {
            BookmarkItemKind.Folder => new BookmarkFolderViewModel(
                RequireTitle(record),
                CreateChildren(record.Id, itemsByParentId, expandedFolderIds, iconImageCache, path),
                isExpanded: expandedFolderIds.Contains(record.Id),
                isRoot: false,
                id: record.Id,
                iconImage: iconImage),

            BookmarkItemKind.Bookmark => new BookmarkViewModel(
                RequireTitle(record),
                RequireUrl(record),
                record.Id,
                iconImage,
                record.IsSecret),

            _ => throw new InvalidOperationException("Unsupported bookmark item kind.")
        };
    }

    private static IImage? GetIconImage(
        BookmarkItemRecord record,
        BookmarkIconImageCache? iconImageCache)
    {
        if (iconImageCache is null)
            return null;

        return record.IsSecret
            ? iconImageCache.GetSecretImage(record.SecretIconAssetId)
            : iconImageCache.GetImage(record.IconAssetId);
    }

    private static string RequireTitle(BookmarkItemRecord record)
    {
        return record.Title ?? throw new InvalidOperationException("Bookmark tree item title is missing.");
    }

    private static string RequireUrl(BookmarkItemRecord record)
    {
        return record.Url ?? throw new InvalidOperationException("Bookmark URL is missing.");
    }

    private static string GetParentKey(string? parentId)
    {
        return parentId ?? RootParentKey;
    }

    private static string? GetDisplayParentId(
        BookmarkItemRecord item,
        Dictionary<string, BookmarkItemRecord> liveItemsById)
    {
        if (item.ParentId is null)
            return null;

        if (!liveItemsById.TryGetValue(item.ParentId, out var parent) ||
            parent.Kind != BookmarkItemKind.Folder ||
            WouldCreateDisplayCycle(item.Id, item.ParentId, liveItemsById))
        {
            return null;
        }

        return item.ParentId;
    }

    private static bool WouldCreateDisplayCycle(
        string itemId,
        string? parentId,
        Dictionary<string, BookmarkItemRecord> liveItemsById)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentParentId = parentId;

        while (currentParentId is not null)
        {
            if (string.Equals(currentParentId, itemId, StringComparison.Ordinal) || !visited.Add(currentParentId))
                return true;

            if (!liveItemsById.TryGetValue(currentParentId, out var parent))
                return false;

            currentParentId = parent.ParentId;
        }

        return false;
    }

    private static readonly IReadOnlySet<string> EmptyExpandedFolderIds = new HashSet<string>(StringComparer.Ordinal);
}
