using System;
using Stranichnik.Icons;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class IconAssetServiceTests
{
    [Fact]
    public void ComputeSha256_returns_lowercase_hex_hash()
    {
        var hash = IconHash.ComputeSha256(OriginalIconBytes);

        Assert.Equal(ExpectedOriginalIconHash, hash);
    }

    [Fact]
    public void GetOrCreateFromOriginalBytes_creates_processed_icon_asset()
    {
        var store = new InMemoryBookmarkTreeStore();
        var processor = new FakeIconImageProcessor();
        var service = CreateService(store, processor);

        var iconAsset = service.GetOrCreateFromOriginalBytes(OriginalIconBytes);

        Assert.Equal("icon-1", iconAsset.Id);
        Assert.Equal(IconHash.Sha256Algorithm, iconAsset.SourceHashAlgorithm);
        Assert.Equal(ExpectedOriginalIconHash, iconAsset.SourceHash);
        Assert.Equal(3, iconAsset.SourceSizeBytes);
        Assert.Equal("image/png", iconAsset.ProcessedMimeType);
        Assert.Equal(64, iconAsset.ProcessedWidth);
        Assert.Equal(64, iconAsset.ProcessedHeight);
        Assert.Equal(ProcessedIconBytes, iconAsset.ProcessedBytes.ToArray());
        Assert.Equal(CreatedAt, iconAsset.CreatedAtUtc);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public void GetOrCreateFromOriginalBytes_reuses_existing_asset_before_processing()
    {
        var store = new InMemoryBookmarkTreeStore();
        var existing = new BookmarkIconAssetRecord(
            "existing",
            IconHash.Sha256Algorithm,
            IconHash.ComputeSha256(OriginalIconBytes),
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            ExistingProcessedIconBytes,
            CreatedAt);
        store.GetOrCreateIconAsset(existing);
        var processor = new FakeIconImageProcessor();
        var service = CreateService(store, processor);

        var iconAsset = service.GetOrCreateFromOriginalBytes(OriginalIconBytes);

        Assert.Same(existing, iconAsset);
        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public void GetOrCreateFromOriginalBytes_rejects_empty_input()
    {
        var store = new InMemoryBookmarkTreeStore();
        var service = CreateService(store, new FakeIconImageProcessor());

        Assert.Throws<ArgumentException>(
            () => service.GetOrCreateFromOriginalBytes(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void GetOrCreateFromOriginalBytes_rejects_oversized_input_before_processing()
    {
        var store = new InMemoryBookmarkTreeStore();
        var processor = new FakeIconImageProcessor();
        var service = CreateService(
            store,
            processor,
            new IconProcessingOptions(MaxInputBytes: 2));

        Assert.Throws<InvalidOperationException>(
            () => service.GetOrCreateFromOriginalBytes(OriginalIconBytes));
        Assert.Equal(0, processor.CallCount);
    }

    private static IconAssetService CreateService(
        IBookmarkTreeStore store,
        IIconImageProcessor processor,
        IconProcessingOptions? options = null)
    {
        return new(
            store,
            processor,
            options,
            idFactory: () => "icon-1",
            clock: () => CreatedAt);
    }

    private sealed class FakeIconImageProcessor : IIconImageProcessor
    {
        public int CallCount { get; private set; }

        public ProcessedIconImage Process(ReadOnlyMemory<byte> originalBytes)
        {
            _ = originalBytes;
            CallCount++;

            return new(
                "image/png",
                Width: 64,
                Height: 64,
                ProcessedIconBytes);
        }
    }

    private const string ExpectedOriginalIconHash =
        "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81";

    private static readonly byte[] OriginalIconBytes = [1, 2, 3];
    private static readonly byte[] ProcessedIconBytes = [9, 8, 7];
    private static readonly byte[] ExistingProcessedIconBytes = [4, 5, 6];
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
