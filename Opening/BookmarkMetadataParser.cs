using System;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Stranichnik.Opening;

public sealed class BookmarkMetadataParser
{
    private const int MaxTitleLength = 300;
    private static readonly Regex TitleTagRegex = new(
        @"<title\b[^>]*>(?<title>.*?)</title>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly HtmlParser _parser = new();

    public BookmarkPageMetadata Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var document = _parser.ParseDocument(html);
        var title =
            ReadMetaContent(document, "property", "og:title") ??
            ReadMetaContent(document, "name", "twitter:title") ??
            CleanTitle(document.Title);

        return new BookmarkPageMetadata(title);
    }

    public static BookmarkPageMetadata ParseTitleTagOnly(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var match = TitleTagRegex.Match(html);
        return new BookmarkPageMetadata(match.Success
            ? CleanTitle(match.Groups["title"].Value)
            : null);
    }

    private static string? ReadMetaContent(IDocument document, string attributeName, string attributeValue)
    {
        foreach (var element in document.QuerySelectorAll("meta"))
        {
            if (!string.Equals(element.GetAttribute(attributeName), attributeValue, StringComparison.OrdinalIgnoreCase))
                continue;

            var title = CleanTitle(element.GetAttribute("content"));
            if (title is not null)
                return title;
        }

        return null;
    }

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var decoded = WebUtility.HtmlDecode(title);
        var normalized = WhitespaceRegex.Replace(decoded, " ").Trim();
        if (normalized.Length == 0)
            return null;

        return normalized.Length <= MaxTitleLength
            ? normalized
            : normalized[..MaxTitleLength].TrimEnd();
    }
}
