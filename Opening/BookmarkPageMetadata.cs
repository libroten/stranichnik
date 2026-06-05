using System;
using System.Collections.Generic;
using Stranichnik.Icons;

namespace Stranichnik.Opening;

public sealed record BookmarkPageMetadata(
    string? Title,
    IReadOnlyList<BookmarkIconCandidate> IconCandidates,
    BookmarkFetchedIcon? Favicon)
{
    public BookmarkPageMetadata(string? title)
        : this(title, [], Favicon: null)
    {
    }
}

public sealed record BookmarkIconCandidate(
    Uri Uri,
    string? Rel,
    string? Type,
    string? Sizes);

public sealed record BookmarkFetchedIcon(
    BookmarkIconCandidate Candidate,
    ReadOnlyMemory<byte> OriginalBytes,
    ProcessedIconImage Image);
