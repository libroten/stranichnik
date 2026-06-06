using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Search;
using Stranichnik.Storage;

namespace Stranichnik.Searching;

internal static class BookmarkSearchDocumentMapper
{
    public static IEnumerable<BookmarkSearchDocument> CreateDocuments(
        BookmarkTreeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Items
            .Where(CanIndex)
            .Select(CreateDocument);
    }

    public static bool CanIndex(BookmarkItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Kind == BookmarkItemKind.Bookmark
            && record.Metadata.DeletedAtUtc is null
            && !string.IsNullOrWhiteSpace(record.Title)
            && !string.IsNullOrWhiteSpace(record.Url);
    }

    public static BookmarkSearchDocument CreateDocument(BookmarkItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new BookmarkSearchDocument(
            record.Id,
            record.Title ?? string.Empty,
            record.Url ?? string.Empty);
    }
}
