namespace Stranichnik.Search.Internal;

internal sealed record Posting(
    string DocumentId,
    IndexedFieldName Field,
    int TermFrequency,
    IReadOnlyList<int> Positions);
