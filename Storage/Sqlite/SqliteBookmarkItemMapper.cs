using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Stranichnik.Storage.Sqlite;

internal static class SqliteBookmarkItemMapper
{
    public const string FolderItemType = "folder";
    public const string BookmarkItemType = "bookmark";

    public static string ToDatabaseValue(BookmarkItemKind kind)
    {
        return kind switch
        {
            BookmarkItemKind.Folder => FolderItemType,
            BookmarkItemKind.Bookmark => BookmarkItemType,
            _ => throw new InvalidOperationException("Unsupported bookmark item kind.")
        };
    }

    public static BookmarkItemKind ToBookmarkItemKind(string value)
    {
        return value switch
        {
            FolderItemType => BookmarkItemKind.Folder,
            BookmarkItemType => BookmarkItemKind.Bookmark,
            _ => throw new InvalidOperationException("Unsupported bookmark item kind.")
        };
    }

    public static string ToDatabaseValue(BookmarkSyncState state)
    {
        return state switch
        {
            BookmarkSyncState.Clean => "clean",
            BookmarkSyncState.Dirty => "dirty",
            BookmarkSyncState.Conflict => "conflict",
            _ => throw new InvalidOperationException("Unsupported bookmark sync state.")
        };
    }

    public static BookmarkSyncState ToBookmarkSyncState(string value)
    {
        return value switch
        {
            "clean" => BookmarkSyncState.Clean,
            "dirty" => BookmarkSyncState.Dirty,
            "conflict" => BookmarkSyncState.Conflict,
            _ => throw new InvalidOperationException("Unsupported bookmark sync state.")
        };
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset ParseDateTime(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : ParseDateTime(reader.GetString(ordinal));
    }

    public static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static byte[]? ReadNullableBytes(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);

        if (reader.IsDBNull(ordinal))
            return null;

        using var stream = reader.GetStream(ordinal);
        using var memory = new System.IO.MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static object ToDatabaseValue(string? value)
    {
        return value is null ? DBNull.Value : value;
    }

    public static object ToDatabaseValue(DateTimeOffset? value)
    {
        return value is null ? DBNull.Value : FormatDateTime(value.Value);
    }

    public static object ToDatabaseValue(byte[]? value)
    {
        return value is null ? DBNull.Value : value;
    }

    public static object ToDatabaseValue(long? value)
    {
        return value is null ? DBNull.Value : value.Value;
    }
}
