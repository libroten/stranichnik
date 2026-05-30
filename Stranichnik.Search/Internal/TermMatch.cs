namespace Stranichnik.Search.Internal;

internal sealed record TermMatch(
    string Term,
    TermMatchKind Kind,
    IReadOnlyDictionary<IndexedFieldName, Dictionary<string, Posting>> FieldPostings);
