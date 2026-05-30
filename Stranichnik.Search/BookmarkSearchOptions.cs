namespace Stranichnik.Search;

/// <summary>
/// Controls how a bookmark search query is executed.
/// </summary>
public sealed record BookmarkSearchOptions
{
    /// <summary>
    /// Gets the maximum number of results to return.
    /// </summary>
    public int MaxResults { get; init; } = 50;

    /// <summary>
    /// Gets the minimum score required for a result to be returned.
    /// </summary>
    public double MinimumScore { get; init; }

    /// <summary>
    /// Gets a value indicating whether development diagnostics should be included.
    /// </summary>
    public bool IncludeDiagnostics { get; init; }
}
