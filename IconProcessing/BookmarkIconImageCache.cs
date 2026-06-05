using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public sealed class BookmarkIconImageCache
{
    private readonly IBookmarkTreeStore _treeStore;
    private readonly Dictionary<string, IImage?> _imagesByAssetId = new(StringComparer.Ordinal);

    public BookmarkIconImageCache(IBookmarkTreeStore treeStore)
    {
        _treeStore = treeStore;
    }

    public IImage? GetImage(string? iconAssetId)
    {
        if (iconAssetId is null)
            return null;

        if (_imagesByAssetId.TryGetValue(iconAssetId, out var cachedImage))
            return cachedImage;

        var image = TryLoadImage(iconAssetId);
        _imagesByAssetId[iconAssetId] = image;
        return image;
    }

    public void Invalidate(string iconAssetId)
    {
        if (_imagesByAssetId.Remove(iconAssetId))
            Logs.Print("Bookmark icon cache entry invalidated.");
    }

    private Bitmap? TryLoadImage(string iconAssetId)
    {
        var iconAsset = _treeStore.GetIconAsset(iconAssetId);

        if (iconAsset is null)
        {
            Logs.Print("Bookmark icon load skipped: icon asset was not found.");
            return null;
        }

        try
        {
            using var stream = new MemoryStream(iconAsset.ProcessedBytes.ToArray(), writable: false);
            var bitmap = new Bitmap(stream);
            Logs.Print(
                $"Bookmark icon loaded from storage. Width={bitmap.PixelSize.Width}, Height={bitmap.PixelSize.Height}, Bytes={iconAsset.ProcessedBytes.Length}.");
            return bitmap;
        }
        catch (ArgumentException)
        {
            Logs.Print("Bookmark icon load failed: bitmap data is invalid.");
            return null;
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Bookmark icon load failed: bitmap decoder error.");
            return null;
        }
    }
}
