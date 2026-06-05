using System;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public sealed class IconAssetService
{
    private readonly IBookmarkTreeStore _treeStore;
    private readonly IIconImageProcessor _imageProcessor;
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IconProcessingOptions _options;

    public IconAssetService(
        IBookmarkTreeStore treeStore,
        IIconImageProcessor imageProcessor,
        IconProcessingOptions? options = null,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _treeStore = treeStore;
        _imageProcessor = imageProcessor;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _options = options ?? new IconProcessingOptions();
    }

    public BookmarkIconAssetRecord GetOrCreateFromOriginalBytes(ReadOnlyMemory<byte> originalBytes)
    {
        if (originalBytes.Length == 0)
        {
            Logs.Print("Icon asset creation rejected: empty input.");
            throw new ArgumentException("Icon image bytes cannot be empty.", nameof(originalBytes));
        }

        if (originalBytes.Length > _options.MaxInputBytes)
        {
            Logs.Print(
                $"Icon asset creation rejected: input is too large. Bytes={originalBytes.Length}, MaxBytes={_options.MaxInputBytes}.");
            throw new InvalidOperationException("Icon image is too large.");
        }

        var sourceHash = IconHash.ComputeSha256(originalBytes.Span);
        var existing = _treeStore.GetIconAssetBySourceHash(IconHash.Sha256Algorithm, sourceHash);

        if (existing is not null)
        {
            Logs.Print("Icon asset reused from storage by source hash.");
            return existing;
        }

        Logs.Print($"Icon image processing started. SourceBytes={originalBytes.Length}.");
        var processed = _imageProcessor.Process(originalBytes);
        Logs.Print(
            $"Icon image processing completed. OutputMime={processed.MimeType}, Width={processed.Width}, Height={processed.Height}, OutputBytes={processed.Bytes.Length}.");

        var record = new BookmarkIconAssetRecord(
            _idFactory(),
            IconHash.Sha256Algorithm,
            sourceHash,
            originalBytes.Length,
            processed.MimeType,
            processed.Width,
            processed.Height,
            processed.Bytes,
            _clock());

        var stored = _treeStore.GetOrCreateIconAsset(record);
        Logs.Print(stored.Id == record.Id
            ? "Icon asset created in storage."
            : "Icon asset reused from storage after processing.");

        return stored;
    }
}
