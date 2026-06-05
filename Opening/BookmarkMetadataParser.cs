using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Regex LinkTagRegex = new(
        @"<link\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly HtmlParser _parser = new();

    public BookmarkPageMetadata Parse(string html)
    {
        return Parse(html, pageUri: null);
    }

    public BookmarkPageMetadata Parse(string html, Uri? pageUri)
    {
        ArgumentNullException.ThrowIfNull(html);

        var document = _parser.ParseDocument(html);
        var title =
            ReadMetaContent(document, "property", "og:title") ??
            ReadMetaContent(document, "name", "twitter:title") ??
            CleanTitle(document.Title);

        return new BookmarkPageMetadata(title, ReadIconCandidates(document, pageUri), Favicon: null);
    }

    public static BookmarkPageMetadata ParseTitleTagOnly(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var match = TitleTagRegex.Match(html);
        return new BookmarkPageMetadata(match.Success
            ? CleanTitle(match.Groups["title"].Value)
            : null);
    }

    public static List<BookmarkIconCandidate> ParseIconCandidatesFromPartialHtml(string html, Uri pageUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageUri);

        var candidates = new List<BookmarkIconCandidate>();

        foreach (Match match in LinkTagRegex.Matches(html))
        {
            var tag = match.Value;
            var rel = ReadTagAttribute(tag, "rel");
            if (!IsSupportedIconRel(rel))
                continue;

            var href = ReadTagAttribute(tag, "href");
            if (!TryResolveUri(pageUri, href, out var uri))
                continue;

            candidates.Add(new BookmarkIconCandidate(
                uri,
                CleanIconAttribute(rel),
                CleanIconAttribute(ReadTagAttribute(tag, "type")),
                CleanIconAttribute(ReadTagAttribute(tag, "sizes"))));
        }

        AddFallbackIconCandidate(candidates, pageUri);
        return candidates;
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

    private static List<BookmarkIconCandidate> ReadIconCandidates(IDocument document, Uri? pageUri)
    {
        if (pageUri is null)
            return [];

        var candidates = new List<BookmarkIconCandidate>();

        foreach (var element in document.QuerySelectorAll("link"))
        {
            var rel = element.GetAttribute("rel");
            if (!IsSupportedIconRel(rel))
                continue;

            var href = element.GetAttribute("href");
            if (!TryResolveUri(pageUri, href, out var uri))
                continue;

            candidates.Add(new BookmarkIconCandidate(
                uri,
                CleanIconAttribute(rel),
                CleanIconAttribute(element.GetAttribute("type")),
                CleanIconAttribute(element.GetAttribute("sizes"))));
        }

        AddFallbackIconCandidate(candidates, pageUri);

        return candidates;
    }

    private static void AddFallbackIconCandidate(List<BookmarkIconCandidate> candidates, Uri pageUri)
    {
        var fallbackUri = new Uri(pageUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico");
        if (!candidates.Any(candidate => candidate.Uri == fallbackUri))
            candidates.Add(new BookmarkIconCandidate(fallbackUri, "fallback", null, null));
    }

    private static string? ReadTagAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(
            tag,
            @"\b" + Regex.Escape(attributeName) + @"\s*=\s*(""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static bool IsSupportedIconRel(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel))
            return false;

        var tokens = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Contains("icon", StringComparer.OrdinalIgnoreCase))
            return true;

        return tokens.Any(token =>
            string.Equals(token, "apple-touch-icon", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "apple-touch-icon-precomposed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveUri(
        Uri pageUri,
        string? href,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(href))
            return false;

        return Uri.TryCreate(pageUri, href.Trim(), out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? CleanIconAttribute(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
