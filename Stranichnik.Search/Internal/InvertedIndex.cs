namespace Stranichnik.Search.Internal;

internal sealed class InvertedIndex
{
    private readonly Dictionary<string, Dictionary<IndexedFieldName, Dictionary<string, Posting>>> _postings =
        new(StringComparer.Ordinal);

    private readonly SortedSet<string> _terms = new(StringComparer.Ordinal);

    private readonly KGramTermIndex _kGramTermIndex = new();

    public void AddDocument(IndexedDocument document)
    {
        foreach (IndexedField field in document.Fields.Values)
        {
            foreach (IGrouping<string, SearchToken> tokenGroup in field.Tokens.GroupBy(
                token => token.Value,
                StringComparer.Ordinal))
            {
                if (!_postings.TryGetValue(tokenGroup.Key, out Dictionary<IndexedFieldName, Dictionary<string, Posting>>? fieldPostings))
                {
                    fieldPostings = [];
                    _postings[tokenGroup.Key] = fieldPostings;
                    _terms.Add(tokenGroup.Key);
                }

                if (!fieldPostings.TryGetValue(field.Name, out Dictionary<string, Posting>? documentPostings))
                {
                    documentPostings = new Dictionary<string, Posting>(StringComparer.Ordinal);
                    fieldPostings[field.Name] = documentPostings;
                }

                int[] positions = tokenGroup.Select(token => token.Position).ToArray();
                documentPostings[document.Id] = new Posting(
                    document.Id,
                    field.Name,
                    positions.Length,
                    positions);

                _kGramTermIndex.AddTerm(tokenGroup.Key);
            }
        }
    }

    public void RemoveDocument(IndexedDocument document)
    {
        foreach (IndexedField field in document.Fields.Values)
        {
            foreach (string term in field.Tokens.Select(token => token.Value).Distinct(StringComparer.Ordinal).ToArray())
            {
                if (!_postings.TryGetValue(term, out Dictionary<IndexedFieldName, Dictionary<string, Posting>>? fieldPostings)
                    || !fieldPostings.TryGetValue(field.Name, out Dictionary<string, Posting>? documentPostings))
                {
                    continue;
                }

                documentPostings.Remove(document.Id);

                if (documentPostings.Count == 0)
                {
                    fieldPostings.Remove(field.Name);
                }

                if (fieldPostings.Count == 0)
                {
                    _postings.Remove(term);
                    _terms.Remove(term);
                }

                _kGramTermIndex.RemoveTerm(term);
            }
        }
    }

    public IReadOnlyList<TermMatch> FindMatches(string queryTerm)
    {
        List<TermMatch> matches = [];

        if (_postings.TryGetValue(queryTerm, out Dictionary<IndexedFieldName, Dictionary<string, Posting>>? exactPostings))
        {
            matches.Add(new TermMatch(queryTerm, TermMatchKind.Exact, exactPostings));
        }

        if (queryTerm.Length < SearchScoringParameters.MinimumPrefixLength)
        {
            return matches;
        }

        foreach (string term in FindPrefixTerms(queryTerm))
        {
            matches.Add(new TermMatch(term, TermMatchKind.Prefix, _postings[term]));
        }

        if (queryTerm.Length < SearchScoringParameters.MinimumFuzzyLength)
        {
            return matches;
        }

        int maximumDistance = EditDistance.GetMaximumDistance(queryTerm);
        HashSet<string> alreadyMatchedTerms = matches.Select(match => match.Term).ToHashSet(StringComparer.Ordinal);

        foreach (string candidateTerm in _kGramTermIndex.FindCandidates(queryTerm))
        {
            if (alreadyMatchedTerms.Contains(candidateTerm))
            {
                continue;
            }

            int distance = EditDistance.CalculateBounded(queryTerm, candidateTerm, maximumDistance);

            if (distance is not (1 or 2) || !_postings.TryGetValue(candidateTerm, out Dictionary<IndexedFieldName, Dictionary<string, Posting>>? fuzzyPostings))
            {
                continue;
            }

            matches.Add(new TermMatch(
                candidateTerm,
                distance == 1 ? TermMatchKind.FuzzyDistanceOne : TermMatchKind.FuzzyDistanceTwo,
                fuzzyPostings));
            alreadyMatchedTerms.Add(candidateTerm);
        }

        return matches;
    }

    public void Clear()
    {
        _postings.Clear();
        _terms.Clear();
        _kGramTermIndex.Clear();
    }

    private IEnumerable<string> FindPrefixTerms(string queryTerm)
    {
        string rangeEnd = queryTerm + char.MaxValue;

        return _terms
            .GetViewBetween(queryTerm, rangeEnd)
            .Where(term => term != queryTerm && term.StartsWith(queryTerm, StringComparison.Ordinal))
            .Take(SearchScoringParameters.MaximumPrefixExpansionsPerQueryToken);
    }
}
