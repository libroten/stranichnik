using System;
using System.Linq;
using Stranichnik.Searching;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkSearchDocumentMapperTests
{
    [Fact]
    public void CreateDocumentsIndexesBookmarksWithVisibleTitleAndUrlOnly()
    {
        var snapshot = new BookmarkTreeSnapshot(
        [
            CreateFolder("folder"),
            CreateBookmark("visible", "Visible", "https://visible.example.com"),
            CreateBookmark("deleted", "Deleted", "https://deleted.example.com") with
            {
                Metadata = CreateMetadata(deletedAtUtc: UpdatedAt),
            },
            CreateBookmark("hidden-secret", title: null, url: null) with
            {
                IsSecret = true,
            },
            CreateBookmark("visible-secret", "Visible Secret", "https://secret.example.com") with
            {
                IsSecret = true,
            },
        ]);

        var documents = BookmarkSearchDocumentMapper.CreateDocuments(snapshot).ToList();

        Assert.Collection(
            documents,
            document =>
            {
                Assert.Equal("visible", document.Id);
                Assert.Equal("Visible", document.Title);
                Assert.Equal("https://visible.example.com", document.Url);
            },
            document =>
            {
                Assert.Equal("visible-secret", document.Id);
                Assert.Equal("Visible Secret", document.Title);
                Assert.Equal("https://secret.example.com", document.Url);
            });
    }

    [Fact]
    public void CreateDocumentNormalizesNullTitleAndUrlToEmptyStrings()
    {
        var record = CreateBookmark("bookmark", title: null, url: null);

        var document = BookmarkSearchDocumentMapper.CreateDocument(record);

        Assert.Equal("bookmark", document.Id);
        Assert.Empty(document.Title);
        Assert.Empty(document.Url);
    }

    private static BookmarkItemRecord CreateFolder(string id)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Folder,
            SortOrder: 1000,
            $"Folder {id}",
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateBookmark(
        string id,
        string? title,
        string? url)
    {
        return new(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            title,
            url,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemMetadata CreateMetadata(DateTimeOffset? deletedAtUtc = null)
    {
        return new(
            CreatedAt,
            CreatedAt,
            deletedAtUtc,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ContentHash: null,
            "test-device");
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
