using System;
using Avalonia.Media;

namespace Stranichnik.Icons;

public enum IconLibraryTargetKind
{
    Bookmark,
    Folder
}

public sealed record IconLibraryRequest(
    IconLibraryTargetKind TargetKind,
    string? TargetUrl,
    bool TargetWillBeSecret);

public sealed record IconLibrarySelection(
    string? RegularIconAssetId,
    string? SecretIconAssetId);

public sealed record IconLibraryItem(
    IconLibrarySelection Selection,
    IImage Preview,
    int PriorityScore,
    int BookmarkUsageCount,
    int FolderUsageCount,
    DateTimeOffset CreatedAtUtc);

public sealed record IconLibraryDialogResult(
    IconLibrarySelection Selection,
    IImage Preview);
