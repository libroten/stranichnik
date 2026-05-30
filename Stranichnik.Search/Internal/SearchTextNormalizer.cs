using System.Text;

namespace Stranichnik.Search.Internal;

internal static class SearchTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Normalize(NormalizationForm.FormKC);
        StringBuilder builder = new(normalized.Length);
        bool previousWasWhitespace = false;

        foreach (char character in normalized)
        {
            char current = char.ToLowerInvariant(character);
            current = current == 'ё' ? 'е' : current;

            if (char.IsWhiteSpace(current))
            {
                if (builder.Length > 0 && !previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(current);
            previousWasWhitespace = false;
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
