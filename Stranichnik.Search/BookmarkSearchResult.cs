namespace Stranichnik.Search;

/// <summary>
/// Represents a ranked search result.
/// </summary>
/// <param name="Id">The caller-owned document identifier.</param>
/// <param name="Score">The relevance score assigned by the search index.</param>
/// <param name="MatchedFields">Field names that contributed to the match.</param>
/// <param name="Diagnostics">Optional development diagnostics requested by the caller.</param>
public sealed record BookmarkSearchResult(
    string Id,
    double Score,
    IReadOnlyList<string> MatchedFields,
    string? Diagnostics = null);
