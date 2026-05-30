using System.Text;

namespace Stranichnik.Search.Internal;

internal static class SearchTokenizer
{
    public const int MaximumTokenLength = 128;
    public const int MaximumTokensPerField = 512;

    public static IReadOnlyList<SearchToken> Tokenize(string? text)
    {
        string normalized = SearchTextNormalizer.Normalize(text);

        if (normalized.Length == 0)
        {
            return [];
        }

        List<SearchToken> tokens = [];
        int index = 0;
        int position = 0;

        while (index < normalized.Length && tokens.Count < MaximumTokensPerField)
        {
            if (TryReadAlias(normalized, index, position, tokens, out int nextIndex))
            {
                index = nextIndex;
                position++;
                continue;
            }

            char current = normalized[index];

            if (!char.IsLetterOrDigit(current))
            {
                index++;
                continue;
            }

            int start = index;
            StringBuilder builder = new(MaximumTokenLength);

            while (index < normalized.Length && char.IsLetterOrDigit(normalized[index]))
            {
                if (builder.Length < MaximumTokenLength)
                {
                    builder.Append(normalized[index]);
                }

                index++;
            }

            if (index > start)
            {
                tokens.Add(new SearchToken(builder.ToString(), position));
                position++;
            }
        }

        return tokens;
    }

    private static bool TryReadAlias(
        string text,
        int index,
        int position,
        List<SearchToken> tokens,
        out int nextIndex)
    {
        if (TryReadSharpAlias(text, index, position, tokens, out nextIndex))
        {
            return true;
        }

        if (TryReadDotNetAlias(text, index, position, tokens, out nextIndex))
        {
            return true;
        }

        nextIndex = index;
        return false;
    }

    private static bool TryReadSharpAlias(
        string text,
        int index,
        int position,
        List<SearchToken> tokens,
        out int nextIndex)
    {
        if (index + 1 >= text.Length || text[index + 1] != '#')
        {
            nextIndex = index;
            return false;
        }

        char current = text[index];
        string? alias = current switch
        {
            'c' => "csharp",
            'f' => "fsharp",
            _ => null
        };

        if (alias is null || !IsBoundary(text, index - 1) || !IsBoundary(text, index + 2))
        {
            nextIndex = index;
            return false;
        }

        AddAliasTokens(tokens, alias, current.ToString(), position);
        nextIndex = index + 2;
        return true;
    }

    private static bool TryReadDotNetAlias(
        string text,
        int index,
        int position,
        List<SearchToken> tokens,
        out int nextIndex)
    {
        const string DotNet = ".net";

        if (index + DotNet.Length > text.Length
            || !text.AsSpan(index, DotNet.Length).SequenceEqual(DotNet)
            || !IsBoundary(text, index - 1)
            || !IsBoundary(text, index + DotNet.Length))
        {
            nextIndex = index;
            return false;
        }

        AddAliasTokens(tokens, "dotnet", "net", position);
        nextIndex = index + DotNet.Length;
        return true;
    }

    private static void AddAliasTokens(
        List<SearchToken> tokens,
        string first,
        string second,
        int position)
    {
        if (tokens.Count < MaximumTokensPerField)
        {
            tokens.Add(new SearchToken(first, position));
        }

        if (tokens.Count < MaximumTokensPerField)
        {
            tokens.Add(new SearchToken(second, position));
        }
    }

    private static bool IsBoundary(string text, int index)
    {
        return index < 0
            || index >= text.Length
            || !char.IsLetterOrDigit(text[index]);
    }
}
