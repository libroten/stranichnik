using System;
using Avalonia;
using Avalonia.Media;
using Stranichnik.Icons;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class IconLibraryServiceTests
{
    [Fact]
    public void Build_excludes_secret_icons_when_secrets_are_hidden()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateBookmark("bookmark", "https://example.com", iconAssetId: "regular-icon"),
            CreateSecretBookmark("secret", secretIconAssetId: "secret-icon")
        ]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "regular-hash"));
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "secret-hash"));
        var service = CreateService(store);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Bookmark, "https://example.com", TargetWillBeSecret: false),
            store.Load(),
            includeSecretIcons: false);

        var item = Assert.Single(items);
        Assert.Equal("regular-icon", item.Selection.RegularIconAssetId);
        Assert.Null(item.Selection.SecretIconAssetId);
    }

    [Fact]
    public void Build_includes_secret_icons_when_secrets_are_visible()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateSecretBookmark("secret", secretIconAssetId: "secret-icon")
        ]);
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "secret-hash"));
        var service = CreateService(store);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Bookmark, "https://example.com", TargetWillBeSecret: true),
            store.Load(),
            includeSecretIcons: true);

        var item = Assert.Single(items);
        Assert.Null(item.Selection.RegularIconAssetId);
        Assert.Equal("secret-icon", item.Selection.SecretIconAssetId);
    }

    [Fact]
    public void Build_deduplicates_regular_and_secret_icons_with_same_source_hash()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateBookmark("bookmark", "https://example.com", iconAssetId: "regular-icon"),
            CreateSecretBookmark("secret", secretIconAssetId: "secret-icon")
        ]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "same-hash"));
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "same-hash"));
        var service = CreateService(store);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Bookmark, "https://example.com", TargetWillBeSecret: true),
            store.Load(),
            includeSecretIcons: true);

        var item = Assert.Single(items);
        Assert.Equal("regular-icon", item.Selection.RegularIconAssetId);
        Assert.Equal("secret-icon", item.Selection.SecretIconAssetId);
    }

    [Fact]
    public void Build_uses_secret_preview_fallback_for_deduplicated_icon()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateBookmark("bookmark", "https://example.com", iconAssetId: "regular-icon"),
            CreateSecretBookmark("secret", secretIconAssetId: "secret-icon")
        ]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "same-hash"));
        store.GetOrCreateSecretIconAsset(CreateSecretIconAsset("secret-icon", "same-hash"));
        var secretPreview = new TestImage();
        var service = new IconLibraryService(
            store,
            _ => null,
            secretIconAssetId => secretIconAssetId is null ? null : secretPreview);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Bookmark, "https://example.com", TargetWillBeSecret: true),
            store.Load(),
            includeSecretIcons: true);

        var item = Assert.Single(items);
        Assert.Same(secretPreview, item.Preview);
    }

    [Fact]
    public void Build_sorts_bookmark_icons_by_best_host_score()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateBookmark("low", "https://other.com", iconAssetId: "low-icon"),
            CreateBookmark("high", "https://mail.google.com", iconAssetId: "high-icon")
        ]);
        store.GetOrCreateIconAsset(CreateIconAsset("low-icon", "low-hash", minutesAgo: 1));
        store.GetOrCreateIconAsset(CreateIconAsset("high-icon", "high-hash", minutesAgo: 2));
        var service = CreateService(store);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Bookmark, "https://docs.google.com", TargetWillBeSecret: false),
            store.Load(),
            includeSecretIcons: false);

        Assert.Equal("high-icon", items[0].Selection.RegularIconAssetId);
        Assert.Equal("low-icon", items[1].Selection.RegularIconAssetId);
    }

    [Fact]
    public void Build_sorts_folder_icons_before_bookmark_only_icons_for_folder_targets()
    {
        var store = new InMemoryBookmarkTreeStore([
            CreateBookmark("bookmark", "https://example.com", iconAssetId: "bookmark-icon"),
            CreateFolder("folder", iconAssetId: "folder-icon")
        ]);
        store.GetOrCreateIconAsset(CreateIconAsset("bookmark-icon", "bookmark-hash", minutesAgo: 1));
        store.GetOrCreateIconAsset(CreateIconAsset("folder-icon", "folder-hash", minutesAgo: 2));
        var service = CreateService(store);

        var items = service.Build(
            new IconLibraryRequest(IconLibraryTargetKind.Folder, TargetUrl: null, TargetWillBeSecret: false),
            store.Load(),
            includeSecretIcons: false);

        Assert.Equal("folder-icon", items[0].Selection.RegularIconAssetId);
        Assert.Equal("bookmark-icon", items[1].Selection.RegularIconAssetId);
    }

    private static IconLibraryService CreateService(InMemoryBookmarkTreeStore store)
    {
        return new IconLibraryService(
            store,
            iconAssetId => iconAssetId is null ? null : new TestImage(),
            secretIconAssetId => secretIconAssetId is null ? null : new TestImage());
    }

    private static BookmarkItemRecord CreateBookmark(
        string id,
        string url,
        string? iconAssetId)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 0,
            Title: id,
            Url: url,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(),
            iconAssetId);
    }

    private static BookmarkItemRecord CreateSecretBookmark(
        string id,
        string? secretIconAssetId)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 0,
            Title: id,
            Url: "https://secret.example.com",
            IsSecret: true,
            EncryptedPayload: null,
            CreateMetadata(),
            IconAssetId: null,
            secretIconAssetId);
    }

    private static BookmarkItemRecord CreateFolder(string id, string? iconAssetId)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Folder,
            SortOrder: 0,
            Title: id,
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(),
            iconAssetId);
    }

    private static BookmarkIconAssetRecord CreateIconAsset(
        string id,
        string sourceHash,
        int minutesAgo = 0)
    {
        return new(
            id,
            "sha256",
            sourceHash,
            SourceSizeBytes: 1,
            ProcessedMimeType: "image/png",
            ProcessedWidth: 32,
            ProcessedHeight: 32,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));
    }

    private static SecretIconAssetRecord CreateSecretIconAsset(string id, string sourceHash)
    {
        return new(
            id,
            "sha256",
            sourceHash,
            SourceSizeBytes: 1,
            ProcessedMimeType: "image/png",
            ProcessedWidth: 32,
            ProcessedHeight: 32,
            new EncryptedSecretIconPayloadRecord(
                ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty,
                PayloadFormatVersion: 1),
            "secret-generation",
            DateTimeOffset.UtcNow);
    }

    private static BookmarkItemMetadata CreateMetadata()
    {
        return new(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ModifiedDeviceId: "test");
    }

    private sealed class TestImage : IImage
    {
        public Size Size => new(32, 32);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            _ = context;
            _ = sourceRect;
            _ = destRect;
        }
    }
}
