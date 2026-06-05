using Avalonia.Media;

namespace Stranichnik.Searching;

public sealed record BookmarkSearchResultItem(
    string Id,
    string Title,
    string Url,
    IImage? IconImage = null)
{
    public bool HasCustomIcon => IconImage is not null;

    public bool HasDefaultIcon => IconImage is null;
}
