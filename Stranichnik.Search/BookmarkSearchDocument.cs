using System.Diagnostics.CodeAnalysis;

namespace Stranichnik.Search;

/// <summary>
/// Represents a bookmark-like document that can be indexed by the search library.
/// </summary>
/// <param name="Id">Stable caller-owned document identifier.</param>
/// <param name="Title">Bookmark title text.</param>
/// <param name="Url">Bookmark URL text.</param>
/// <param name="Tags">Optional caller-provided tags.</param>
/// <param name="Notes">Optional caller-provided notes.</param>
[SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "The search library must accept malformed and schemeless URL-like text for best-effort indexing.")]
[SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "The search library must preserve caller-provided URL text and tolerate malformed URLs.")]
public sealed record BookmarkSearchDocument(
    string Id,
    string Title,
    string Url,
    IReadOnlyList<string>? Tags = null,
    string? Notes = null);
