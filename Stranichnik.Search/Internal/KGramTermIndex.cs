namespace Stranichnik.Search.Internal;

internal sealed class KGramTermIndex
{
    private const int GramLength = 3;

    private readonly Dictionary<string, HashSet<string>> _termsByGram = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _termReferenceCounts = new(StringComparer.Ordinal);

    public void AddTerm(string term)
    {
        if (!_termReferenceCounts.TryAdd(term, 1))
        {
            _termReferenceCounts[term]++;
            return;
        }

        foreach (string gram in GetGrams(term))
        {
            if (!_termsByGram.TryGetValue(gram, out HashSet<string>? terms))
            {
                terms = new HashSet<string>(StringComparer.Ordinal);
                _termsByGram[gram] = terms;
            }

            terms.Add(term);
        }
    }

    public void RemoveTerm(string term)
    {
        if (!_termReferenceCounts.TryGetValue(term, out int referenceCount))
        {
            return;
        }

        if (referenceCount > 1)
        {
            _termReferenceCounts[term] = referenceCount - 1;
            return;
        }

        _termReferenceCounts.Remove(term);

        foreach (string gram in GetGrams(term))
        {
            if (!_termsByGram.TryGetValue(gram, out HashSet<string>? terms))
            {
                continue;
            }

            terms.Remove(term);

            if (terms.Count == 0)
            {
                _termsByGram.Remove(gram);
            }
        }
    }

    public string[] FindCandidates(string queryTerm)
    {
        Dictionary<string, int> overlapByTerm = new(StringComparer.Ordinal);
        HashSet<string> queryGrams = GetGrams(queryTerm).ToHashSet(StringComparer.Ordinal);

        if (queryGrams.Count == 0)
        {
            return [];
        }

        foreach (string gram in queryGrams)
        {
            if (!_termsByGram.TryGetValue(gram, out HashSet<string>? terms))
            {
                continue;
            }

            foreach (string term in terms)
            {
                overlapByTerm.TryGetValue(term, out int currentOverlap);
                overlapByTerm[term] = currentOverlap + 1;
            }
        }

        int minimumOverlap = Math.Max(1, queryGrams.Count / 3);

        return overlapByTerm
            .Where(pair => pair.Value >= minimumOverlap)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(SearchScoringParameters.MaximumFuzzyCandidateTermsPerQueryToken)
            .Select(pair => pair.Key)
            .ToArray();
    }

    public void Clear()
    {
        _termsByGram.Clear();
        _termReferenceCounts.Clear();
    }

    private static IEnumerable<string> GetGrams(string term)
    {
        if (term.Length < GramLength)
        {
            yield break;
        }

        for (int index = 0; index <= term.Length - GramLength; index++)
        {
            yield return term.Substring(index, GramLength);
        }
    }
}
