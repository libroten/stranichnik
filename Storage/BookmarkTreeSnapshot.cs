using System.Collections.Generic;

namespace Stranichnik.Storage;

public sealed record BookmarkTreeSnapshot(
    IReadOnlyList<BookmarkItemRecord> Items);
