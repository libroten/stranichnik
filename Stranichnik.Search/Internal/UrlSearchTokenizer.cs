namespace Stranichnik.Search.Internal;

internal static class UrlSearchTokenizer
{
    private static readonly char[] HostSeparators = ['.', '-', '_'];
    private static readonly char[] PathSeparators = ['/', '\\', '.', '-', '_', '=', '&', '?', '#'];
    private static readonly char[] MalformedUrlSeparators = ['.', '-', '_', '/', '\\', '=', '&', '?', '#'];

    public static UrlSearchTokens Tokenize(string? url)
    {
        string rawUrl = url ?? string.Empty;
        IReadOnlyList<SearchToken> urlText = SearchTokenizer.Tokenize(rawUrl);

        if (!TryCreateUri(rawUrl, out Uri? uri))
        {
            return new UrlSearchTokens(
                urlText,
                [],
                TokenizeParts(SplitMalformedUrl(rawUrl)),
                []);
        }

        ArgumentNullException.ThrowIfNull(uri);

        IReadOnlyList<SearchToken> hostTokens = TokenizeHost(uri);
        IReadOnlyList<SearchToken> domainPartTokens = TokenizeParts(GetHostVariants(uri).SelectMany(SplitHost));
        IReadOnlyList<SearchToken> pathPartTokens = TokenizeParts(GetPathParts(uri));

        return new UrlSearchTokens(
            urlText,
            hostTokens,
            domainPartTokens,
            pathPartTokens);
    }

    private static bool TryCreateUri(string rawUrl, out Uri? uri)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out uri))
        {
            return true;
        }

        if (!LooksLikeSchemelessHost(rawUrl))
        {
            uri = null;
            return false;
        }

        return Uri.TryCreate("https://" + rawUrl, UriKind.Absolute, out uri);
    }

    private static bool LooksLikeSchemelessHost(string rawUrl)
    {
        return !string.IsNullOrWhiteSpace(rawUrl)
            && rawUrl.Contains('.', StringComparison.Ordinal)
            && !rawUrl.Any(char.IsWhiteSpace)
            && !rawUrl.Contains("://", StringComparison.Ordinal);
    }

    private static List<SearchToken> TokenizeHost(Uri uri)
    {
        List<SearchToken> tokens = [];
        int position = 0;

        foreach (string host in GetHostVariants(uri))
        {
            if (tokens.Count >= SearchTokenizer.MaximumTokensPerField)
            {
                return tokens;
            }

            tokens.Add(new SearchToken(TrimToken(host), position));
            position++;
        }

        return tokens;
    }

    private static HashSet<string> GetHostVariants(Uri uri)
    {
        HashSet<string> variants = new(StringComparer.Ordinal);

        AddHostVariant(variants, uri.Host);

        try
        {
            AddHostVariant(variants, uri.IdnHost);
        }
        catch (UriFormatException)
        {
            // Some malformed IDN inputs can still produce a URI but fail IDN conversion.
        }

        try
        {
            AddHostVariant(variants, uri.DnsSafeHost);
        }
        catch (UriFormatException)
        {
            // Keep URL tokenization forgiving for unusual host data.
        }

        return variants;
    }

    private static void AddHostVariant(HashSet<string> variants, string? host)
    {
        string normalized = SearchTextNormalizer.Normalize(host);

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized["www.".Length..];
        }

        if (normalized.Length > 0)
        {
            variants.Add(normalized);
        }
    }

    private static IEnumerable<string> SplitHost(string host)
    {
        return host.Split(HostSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> GetPathParts(Uri uri)
    {
        string pathAndQuery = string.Concat(uri.AbsolutePath, "?", uri.Query, "#", uri.Fragment);

        foreach (string part in pathAndQuery.Split(
            PathSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return UnescapeSafely(part);
        }
    }

    private static string UnescapeSafely(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string[] SplitMalformedUrl(string rawUrl)
    {
        return rawUrl.Split(
            MalformedUrlSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static List<SearchToken> TokenizeParts(IEnumerable<string> parts)
    {
        List<SearchToken> tokens = [];
        int position = 0;

        foreach (string part in parts)
        {
            foreach (SearchToken token in SearchTokenizer.Tokenize(part))
            {
                if (tokens.Count >= SearchTokenizer.MaximumTokensPerField)
                {
                    return tokens;
                }

                tokens.Add(token with { Position = position });
                position++;
            }
        }

        return tokens;
    }

    private static string TrimToken(string token)
    {
        return token.Length <= SearchTokenizer.MaximumTokenLength
            ? token
            : token[..SearchTokenizer.MaximumTokenLength];
    }
}
