namespace Stranichnik.Search.Internal;

internal sealed record IndexedField(
    IndexedFieldName Name,
    IReadOnlyList<SearchToken> Tokens);
