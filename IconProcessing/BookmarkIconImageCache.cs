using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stranichnik.Diagnostics;
using Stranichnik.Security;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public sealed class BookmarkIconImageCache
{
    private readonly IBookmarkTreeStore _treeStore;
    private readonly ISecretSessionService? _secretSession;
    private readonly Dictionary<string, IImage?> _imagesByAssetId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IImage?> _secretImagesByAssetId = new(StringComparer.Ordinal);

    public BookmarkIconImageCache(
        IBookmarkTreeStore treeStore,
        ISecretSessionService? secretSession = null)
    {
        _treeStore = treeStore;
        _secretSession = secretSession;
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

    public IImage? GetSecretImage(string? secretIconAssetId)
    {
        if (secretIconAssetId is null)
            return null;

        if (_secretImagesByAssetId.TryGetValue(secretIconAssetId, out var cachedImage))
            return cachedImage;

        var image = TryLoadSecretImage(secretIconAssetId);
        _secretImagesByAssetId[secretIconAssetId] = image;
        return image;
    }

    public void Invalidate(string iconAssetId)
    {
        if (_imagesByAssetId.Remove(iconAssetId))
            Logs.Print("Bookmark icon cache entry invalidated.");
    }

    public void ClearSecretImages()
    {
        if (_secretImagesByAssetId.Count == 0)
            return;

        _secretImagesByAssetId.Clear();
        Logs.Print("Secret bookmark icon cache cleared.");
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

    private Bitmap? TryLoadSecretImage(string secretIconAssetId)
    {
        if (_secretSession is null)
        {
            Logs.Print("Secret bookmark icon load skipped: secret icon cache is not configured.");
            return null;
        }

        var dataKey = _secretSession.BorrowDataKey();
        if (dataKey is null)
        {
            Logs.Print("Secret bookmark icon load skipped: secret session is locked.");
            return null;
        }

        var iconAsset = _treeStore.GetSecretIconAsset(secretIconAssetId);
        if (iconAsset is null)
        {
            Logs.Print("Secret bookmark icon load skipped: secret icon asset was not found.");
            return null;
        }

        try
        {
            var processedBytes = SecretIconCryptoService.DecryptProcessedIconBytes(iconAsset, dataKey);
            using var stream = new MemoryStream(processedBytes, writable: false);
            var bitmap = new Bitmap(stream);
            Logs.Print(
                $"Secret bookmark icon loaded from storage. Width={bitmap.PixelSize.Width}, Height={bitmap.PixelSize.Height}, Bytes={processedBytes.Length}.");
            return bitmap;
        }
        catch (ArgumentException)
        {
            Logs.Print("Secret bookmark icon load failed: bitmap data is invalid.");
            return null;
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Secret bookmark icon load failed: bitmap decoder error.");
            return null;
        }
        catch (SecretPayloadException)
        {
            Logs.Print("Secret bookmark icon load failed: decryption failed.");
            return null;
        }
    }
}
