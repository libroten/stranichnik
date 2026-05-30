using System.Text;

namespace Stranichnik.Search.Internal;

internal sealed class SearchDiagnosticsBuilder
{
    private readonly SortedSet<string> _parts = new(StringComparer.Ordinal);

    public void AddMatch(TermMatchKind kind, IndexedFieldName field, string queryTerm, string matchedTerm)
    {
        string matchKind = kind switch
        {
            TermMatchKind.Exact => "exact",
            TermMatchKind.Prefix => "prefix",
            TermMatchKind.FuzzyDistanceOne => "fuzzy1",
            TermMatchKind.FuzzyDistanceTwo => "fuzzy2",
            _ => kind.ToString()
        };

        _parts.Add($"{matchKind} {field.ToResultFieldName()}: {queryTerm}->{matchedTerm}");
    }

    public void AddCoverage(int matchedQueryTokens, int queryTokens)
    {
        _parts.Add($"coverage: {matchedQueryTokens}/{queryTokens}");
    }

    public void AddBonus(string bonus)
    {
        _parts.Add($"bonus: {bonus}");
    }

    public string Build()
    {
        if (_parts.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();

        foreach (string part in _parts)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(part);
        }

        return builder.ToString();
    }
}
