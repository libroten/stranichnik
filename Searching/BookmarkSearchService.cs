using System;
using System.Collections.Generic;
using Stranichnik.Search;
using Stranichnik.Storage;

namespace Stranichnik.Searching;

public sealed class BookmarkSearchService
{
    private readonly IBookmarkSearchIndex _index;

    public BookmarkSearchService(IBookmarkSearchIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        _index = index;
    }

    public void Rebuild(BookmarkTreeSnapshot snapshot)
    {
        _index.Rebuild(BookmarkSearchDocumentMapper.CreateDocuments(snapshot));
    }

    public void AddOrUpdate(BookmarkItemRecord record)
    {
        if (BookmarkSearchDocumentMapper.CanIndex(record))
        {
            _index.AddOrUpdate(BookmarkSearchDocumentMapper.CreateDocument(record));
            return;
        }

        _index.Remove(record.Id);
    }

    public bool Remove(string id)
    {
        return _index.Remove(id);
    }

    public IReadOnlyList<BookmarkSearchResult> Search(
        string query,
        BookmarkSearchOptions? options = null)
    {
        return _index.Search(query, options);
    }
}
