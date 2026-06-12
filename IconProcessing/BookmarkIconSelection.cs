using System;

namespace Stranichnik.Icons;

public sealed record BookmarkIconSelection(
    BookmarkIconSelectionKind Kind,
    ReadOnlyMemory<byte> OriginalBytes,
    IconLibrarySelection? LibrarySelection = null)
{
    public static BookmarkIconSelection KeepExisting { get; } = new(
        BookmarkIconSelectionKind.KeepExisting,
        ReadOnlyMemory<byte>.Empty);

    public static BookmarkIconSelection UseDefault { get; } = new(
        BookmarkIconSelectionKind.UseDefault,
        ReadOnlyMemory<byte>.Empty);

    public static BookmarkIconSelection FromOriginalBytes(ReadOnlyMemory<byte> originalBytes)
    {
        if (originalBytes.Length == 0)
            throw new ArgumentException("Icon image bytes cannot be empty.", nameof(originalBytes));

        return new(BookmarkIconSelectionKind.UseOriginalBytes, originalBytes);
    }

    public static BookmarkIconSelection FromLibrary(IconLibrarySelection librarySelection)
    {
        ArgumentNullException.ThrowIfNull(librarySelection);

        if (librarySelection is { RegularIconAssetId: null, SecretIconAssetId: null })
            throw new ArgumentException("Icon library selection must reference an icon asset.", nameof(librarySelection));

        return new(
            BookmarkIconSelectionKind.UseLibraryIcon,
            ReadOnlyMemory<byte>.Empty,
            librarySelection);
    }
}

public enum BookmarkIconSelectionKind
{
    KeepExisting,
    UseDefault,
    UseOriginalBytes,
    UseLibraryIcon
}
