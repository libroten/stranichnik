using System;

namespace Stranichnik.Icons;

public sealed record BookmarkIconSelection(
    BookmarkIconSelectionKind Kind,
    ReadOnlyMemory<byte> OriginalBytes)
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
}

public enum BookmarkIconSelectionKind
{
    KeepExisting,
    UseDefault,
    UseOriginalBytes
}
