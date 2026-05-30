namespace Stranichnik.Search.Internal;

internal sealed record IndexedDocument(
    string Id,
    string NormalizedTitle,
    string NormalizedUrl,
    string NormalizedHostText,
    IReadOnlyDictionary<IndexedFieldName, IndexedField> Fields);
