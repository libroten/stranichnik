namespace Stranichnik.Search;

/// <summary>
/// Defines an in-memory bookmark search index.
/// </summary>
public interface IBookmarkSearchIndex
{
    /// <summary>
    /// Replaces all indexed documents with the supplied document sequence.
    /// </summary>
    /// <param name="documents">Documents to index.</param>
    void Rebuild(IEnumerable<BookmarkSearchDocument> documents);

    /// <summary>
    /// Adds a new document or replaces an existing document with the same ID.
    /// </summary>
    /// <param name="document">Document to add or update.</param>
    void AddOrUpdate(BookmarkSearchDocument document);

    /// <summary>
    /// Removes a document by ID.
    /// </summary>
    /// <param name="id">Document ID to remove.</param>
    /// <returns><see langword="true" /> when a document was removed; otherwise <see langword="false" />.</returns>
    bool Remove(string id);

    /// <summary>
    /// Clears all indexed documents.
    /// </summary>
    void Clear();

    /// <summary>
    /// Searches the index.
    /// </summary>
    /// <param name="query">User query text.</param>
    /// <param name="options">Optional search settings.</param>
    /// <returns>Ranked result IDs and metadata.</returns>
    IReadOnlyList<BookmarkSearchResult> Search(
        string query,
        BookmarkSearchOptions? options = null);
}
