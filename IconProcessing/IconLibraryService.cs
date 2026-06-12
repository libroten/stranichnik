using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public sealed class IconLibraryService
{
    private readonly IBookmarkTreeStore _treeStore;
    private readonly Func<string?, IImage?> _regularImageProvider;
    private readonly Func<string?, IImage?> _secretImageProvider;

    public IconLibraryService(
        IBookmarkTreeStore treeStore,
        BookmarkIconImageCache iconImageCache)
        : this(
            treeStore,
            iconImageCache.GetImage,
            iconImageCache.GetSecretImage)
    {
    }

    public IconLibraryService(
        IBookmarkTreeStore treeStore,
        Func<string?, IImage?> regularImageProvider,
        Func<string?, IImage?> secretImageProvider)
    {
        ArgumentNullException.ThrowIfNull(treeStore);
        ArgumentNullException.ThrowIfNull(regularImageProvider);
        ArgumentNullException.ThrowIfNull(secretImageProvider);

        _treeStore = treeStore;
        _regularImageProvider = regularImageProvider;
        _secretImageProvider = secretImageProvider;
    }

    public List<IconLibraryItem> Build(
        IconLibraryRequest request,
        BookmarkTreeSnapshot visibleSnapshot,
        bool includeSecretIcons)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(visibleSnapshot);

        var entriesByHash = new Dictionary<IconHashKey, IconLibraryEntryBuilder>();

        foreach (var item in visibleSnapshot.Items)
        {
            if (item.IconAssetId is not null)
                AddRegularIconUsage(entriesByHash, item);

            if (includeSecretIcons && item.IsSecret && item.SecretIconAssetId is not null)
                AddSecretIconUsage(entriesByHash, item);
        }

        var items = entriesByHash
            .Values
            .Select(entry => entry.ToItem(request, _regularImageProvider, _secretImageProvider))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        SortItems(items, request.TargetKind);
        Logs.Print(
            "Icon library built. " +
            $"Count={items.Count}, IncludeSecretIcons={includeSecretIcons}, TargetKind={request.TargetKind}.");
        return items;
    }

    private void AddRegularIconUsage(
        Dictionary<IconHashKey, IconLibraryEntryBuilder> entriesByHash,
        BookmarkItemRecord item)
    {
        var iconAsset = _treeStore.GetIconAsset(item.IconAssetId!);
        if (iconAsset is null)
        {
            Logs.Print("Icon library skipped missing regular icon asset reference.");
            return;
        }

        var entry = GetOrAddEntry(entriesByHash, iconAsset.SourceHashAlgorithm, iconAsset.SourceHash);
        entry.RegularIconAssetId ??= iconAsset.Id;
        entry.CreatedAtUtc = Max(entry.CreatedAtUtc, iconAsset.CreatedAtUtc);
        entry.AddUsage(item);
    }

    private void AddSecretIconUsage(
        Dictionary<IconHashKey, IconLibraryEntryBuilder> entriesByHash,
        BookmarkItemRecord item)
    {
        var secretIconAsset = _treeStore.GetSecretIconAsset(item.SecretIconAssetId!);
        if (secretIconAsset is null)
        {
            Logs.Print("Icon library skipped missing secret icon asset reference.");
            return;
        }

        var entry = GetOrAddEntry(
            entriesByHash,
            secretIconAsset.SourceHashAlgorithm,
            secretIconAsset.SourceHash);
        entry.SecretIconAssetId ??= secretIconAsset.Id;
        entry.CreatedAtUtc = Max(entry.CreatedAtUtc, secretIconAsset.CreatedAtUtc);
        entry.AddUsage(item);
    }

    private static IconLibraryEntryBuilder GetOrAddEntry(
        Dictionary<IconHashKey, IconLibraryEntryBuilder> entriesByHash,
        string sourceHashAlgorithm,
        string sourceHash)
    {
        var key = new IconHashKey(sourceHashAlgorithm, sourceHash);
        if (entriesByHash.TryGetValue(key, out var entry))
            return entry;

        entry = new IconLibraryEntryBuilder();
        entriesByHash[key] = entry;
        return entry;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second)
    {
        return first >= second ? first : second;
    }

    private static void SortItems(List<IconLibraryItem> items, IconLibraryTargetKind targetKind)
    {
        if (targetKind == IconLibraryTargetKind.Folder)
        {
            items.Sort(CompareForFolder);
            return;
        }

        items.Sort(CompareForBookmark);
    }

    private static int CompareForBookmark(IconLibraryItem left, IconLibraryItem right)
    {
        return CompareDescending(left.PriorityScore, right.PriorityScore)
            .Then(CompareDescending(left.BookmarkUsageCount, right.BookmarkUsageCount))
            .Then(CompareDescending(left.FolderUsageCount, right.FolderUsageCount))
            .Then(CompareDescending(left.CreatedAtUtc, right.CreatedAtUtc))
            .Then(CompareAscending(left.Selection.RegularIconAssetId, right.Selection.RegularIconAssetId))
            .Then(CompareAscending(left.Selection.SecretIconAssetId, right.Selection.SecretIconAssetId));
    }

    private static int CompareForFolder(IconLibraryItem left, IconLibraryItem right)
    {
        return CompareDescending(left.FolderUsageCount > 0, right.FolderUsageCount > 0)
            .Then(CompareDescending(left.FolderUsageCount, right.FolderUsageCount))
            .Then(CompareDescending(left.BookmarkUsageCount + left.FolderUsageCount, right.BookmarkUsageCount + right.FolderUsageCount))
            .Then(CompareDescending(left.CreatedAtUtc, right.CreatedAtUtc))
            .Then(CompareAscending(left.Selection.RegularIconAssetId, right.Selection.RegularIconAssetId))
            .Then(CompareAscending(left.Selection.SecretIconAssetId, right.Selection.SecretIconAssetId));
    }

    private static int CompareDescending<T>(T left, T right)
        where T : IComparable<T>
    {
        return right.CompareTo(left);
    }

    private static int CompareAscending(string? left, string? right)
    {
        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private sealed record IconHashKey(string Algorithm, string Hash);

    private sealed class IconLibraryEntryBuilder
    {
        private readonly List<string> _bookmarkUrls = [];

        public string? RegularIconAssetId { get; set; }

        public string? SecretIconAssetId { get; set; }

        public int BookmarkUsageCount { get; private set; }

        public int FolderUsageCount { get; private set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public void AddUsage(BookmarkItemRecord item)
        {
            if (item.Kind == BookmarkItemKind.Folder)
            {
                FolderUsageCount++;
                return;
            }

            BookmarkUsageCount++;
            if (!string.IsNullOrWhiteSpace(item.Url))
                _bookmarkUrls.Add(item.Url);
        }

        public IconLibraryItem? ToItem(
            IconLibraryRequest request,
            Func<string?, IImage?> regularImageProvider,
            Func<string?, IImage?> secretImageProvider)
        {
            var preview = RegularIconAssetId is not null
                ? regularImageProvider(RegularIconAssetId)
                : null;

            preview ??= secretImageProvider(SecretIconAssetId);

            if (preview is null)
            {
                Logs.Print("Icon library skipped icon candidate because preview could not be loaded.");
                return null;
            }

            return new IconLibraryItem(
                new IconLibrarySelection(RegularIconAssetId, SecretIconAssetId),
                preview,
                GetPriorityScore(request),
                BookmarkUsageCount,
                FolderUsageCount,
                CreatedAtUtc);
        }

        private int GetPriorityScore(IconLibraryRequest request)
        {
            if (request.TargetKind != IconLibraryTargetKind.Bookmark)
                return 0;

            var priorityScore = 0;
            foreach (var bookmarkUrl in _bookmarkUrls)
                priorityScore = Math.Max(priorityScore, IconLibraryHostScore.Compute(request.TargetUrl, bookmarkUrl));

            return priorityScore;
        }
    }
}

internal static class IconLibraryComparisonExtensions
{
    public static int Then(this int currentResult, int nextResult)
    {
        return currentResult != 0 ? currentResult : nextResult;
    }
}
