namespace Stranichnik.Search.Internal;

internal sealed record UrlSearchTokens(
    IReadOnlyList<SearchToken> UrlText,
    IReadOnlyList<SearchToken> UrlHost,
    IReadOnlyList<SearchToken> UrlDomainParts,
    IReadOnlyList<SearchToken> UrlPathParts);
