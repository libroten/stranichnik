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

    public static ObservableCollection<BookmarkTreeItemViewModel> CreateViewModels(
        BookmarkTreeSnapshot snapshot,
        IReadOnlySet<string>? expandedFolderIds = null,
        BookmarkIconImageCache? iconImageCache = null)
    {
        var itemsByParentId = snapshot.Items
            .Where(item => item.Metadata.DeletedAtUtc is null)
            .GroupBy(item => GetParentKey(item.ParentId), StringComparer.Ordinal)
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
            expandedFolderIds ?? EmptyExpandedFolderIds,
            iconImageCache,
            []);
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
        return iconImageCache?.GetImage(record.IconAssetId);
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

    private static readonly IReadOnlySet<string> EmptyExpandedFolderIds = new HashSet<string>(StringComparer.Ordinal);
}
