using Stranichnik.Search.Internal;

namespace Stranichnik.Search;

/// <summary>
/// In-memory implementation of <see cref="IBookmarkSearchIndex" />.
/// </summary>
/// <remarks>
/// The first implementation is not thread-safe. Callers must not search and mutate the same
/// instance concurrently.
/// </remarks>
public sealed class InMemoryBookmarkSearchIndex : IBookmarkSearchIndex
{
    private static readonly IReadOnlyList<BookmarkSearchResult> EmptyResults = [];

    private Dictionary<string, IndexedDocument> _indexedDocumentsById =
        new(StringComparer.Ordinal);

    private InvertedIndex _invertedIndex = new();

    private Dictionary<IndexedFieldName, FieldLengthStatistics> _fieldLengthStatistics = [];

    /// <inheritdoc />
    public void Rebuild(IEnumerable<BookmarkSearchDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        Dictionary<string, IndexedDocument> indexedDocumentsById = new(StringComparer.Ordinal);
        InvertedIndex invertedIndex = new();
        Dictionary<IndexedFieldName, FieldLengthStatistics> fieldLengthStatistics = [];

        foreach (BookmarkSearchDocument document in documents)
        {
            BookmarkSearchDocument normalizedDocument = NormalizeDocument(document);
            IndexedDocument indexedDocument = CreateIndexedDocument(normalizedDocument);

            if (indexedDocumentsById.Remove(indexedDocument.Id, out IndexedDocument? previousDocument))
            {
                invertedIndex.RemoveDocument(previousDocument);
                RemoveFieldLengthStatistics(fieldLengthStatistics, previousDocument);
            }

            indexedDocumentsById[indexedDocument.Id] = indexedDocument;
            invertedIndex.AddDocument(indexedDocument);
            AddFieldLengthStatistics(fieldLengthStatistics, indexedDocument);
        }

        _indexedDocumentsById = indexedDocumentsById;
        _invertedIndex = invertedIndex;
        _fieldLengthStatistics = fieldLengthStatistics;
    }

    /// <inheritdoc />
    public void AddOrUpdate(BookmarkSearchDocument document)
    {
        BookmarkSearchDocument normalizedDocument = NormalizeDocument(document);
        IndexedDocument indexedDocument = CreateIndexedDocument(normalizedDocument);
        ReplaceIndexedDocument(indexedDocument);
    }

    /// <inheritdoc />
    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!_indexedDocumentsById.Remove(id, out IndexedDocument? indexedDocument))
        {
            return false;
        }

        _invertedIndex.RemoveDocument(indexedDocument);
        RemoveFieldLengthStatistics(indexedDocument);
        return true;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _indexedDocumentsById.Clear();
        _invertedIndex.Clear();
        _fieldLengthStatistics.Clear();
    }

    /// <inheritdoc />
    public IReadOnlyList<BookmarkSearchResult> Search(
        string query,
        BookmarkSearchOptions? options = null)
    {
        BookmarkSearchOptions effectiveOptions = options ?? new BookmarkSearchOptions();
        ValidateOptions(effectiveOptions);

        if (effectiveOptions.MaxResults == 0 || string.IsNullOrWhiteSpace(query))
        {
            return EmptyResults;
        }

        IReadOnlyList<SearchToken> queryTokens = SearchTokenizer.Tokenize(query);

        if (queryTokens.Count == 0)
        {
            return EmptyResults;
        }

        Dictionary<string, CandidateResult> candidates = new(StringComparer.Ordinal);

        for (int queryTokenIndex = 0; queryTokenIndex < queryTokens.Count; queryTokenIndex++)
        {
            SearchToken queryToken = queryTokens[queryTokenIndex];

            foreach (TermMatch termMatch in _invertedIndex.FindMatches(queryToken.Value))
            {
                foreach ((IndexedFieldName field, Dictionary<string, Posting> documentPostings) in termMatch.FieldPostings)
                {
                    int documentFrequency = documentPostings.Count;
                    double averageFieldLength = GetAverageFieldLength(field);

                    foreach (Posting posting in documentPostings.Values)
                    {
                        if (!candidates.TryGetValue(posting.DocumentId, out CandidateResult? candidate))
                        {
                            candidate = new CandidateResult(posting.DocumentId);
                            candidates[posting.DocumentId] = candidate;
                        }

                        int fieldLength = GetFieldLength(posting.DocumentId, field);
                        candidate.Score += SearchScorer.ScoreExactMatch(
                            field,
                            posting.TermFrequency,
                            _indexedDocumentsById.Count,
                            documentFrequency,
                            fieldLength,
                            averageFieldLength,
                            termMatch.Kind);
                        candidate.MatchedFields.Add(field);
                        candidate.MatchedQueryTokenIndexes.Add(queryTokenIndex);

                        if (effectiveOptions.IncludeDiagnostics)
                        {
                            candidate.Diagnostics.AddMatch(termMatch.Kind, field, queryToken.Value, termMatch.Term);
                        }
                    }
                }
            }
        }

        ApplyQualityBonuses(candidates.Values, queryTokens, query, effectiveOptions.IncludeDiagnostics);

        return candidates.Values
            .Where(candidate => candidate.Score >= effectiveOptions.MinimumScore)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => _indexedDocumentsById[candidate.Id].NormalizedTitle, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Take(effectiveOptions.MaxResults)
            .Select(candidate => new BookmarkSearchResult(
                candidate.Id,
                candidate.Score,
                candidate.MatchedFields
                    .OrderBy(field => field.ToResultFieldName(), StringComparer.Ordinal)
                    .Select(field => field.ToResultFieldName())
                    .ToArray(),
                Diagnostics: effectiveOptions.IncludeDiagnostics ? candidate.Diagnostics.Build() : null))
            .ToArray();
    }

    private void ReplaceIndexedDocument(IndexedDocument indexedDocument)
    {
        if (_indexedDocumentsById.Remove(indexedDocument.Id, out IndexedDocument? previousDocument))
        {
            _invertedIndex.RemoveDocument(previousDocument);
            RemoveFieldLengthStatistics(previousDocument);
        }

        _indexedDocumentsById[indexedDocument.Id] = indexedDocument;
        _invertedIndex.AddDocument(indexedDocument);
        AddFieldLengthStatistics(indexedDocument);
    }

    private void AddFieldLengthStatistics(IndexedDocument document)
    {
        AddFieldLengthStatistics(_fieldLengthStatistics, document);
    }

    private static void AddFieldLengthStatistics(
        Dictionary<IndexedFieldName, FieldLengthStatistics> fieldLengthStatistics,
        IndexedDocument document)
    {
        foreach (IndexedField field in document.Fields.Values)
        {
            if (!fieldLengthStatistics.TryGetValue(field.Name, out FieldLengthStatistics? statistics))
            {
                statistics = new FieldLengthStatistics();
                fieldLengthStatistics[field.Name] = statistics;
            }

            statistics.Add(field.Tokens.Count);
        }
    }

    private void RemoveFieldLengthStatistics(IndexedDocument document)
    {
        RemoveFieldLengthStatistics(_fieldLengthStatistics, document);
    }

    private static void RemoveFieldLengthStatistics(
        Dictionary<IndexedFieldName, FieldLengthStatistics> fieldLengthStatistics,
        IndexedDocument document)
    {
        foreach (IndexedField field in document.Fields.Values)
        {
            if (!fieldLengthStatistics.TryGetValue(field.Name, out FieldLengthStatistics? statistics))
            {
                continue;
            }

            statistics.Remove(field.Tokens.Count);

            if (statistics.IsEmpty)
            {
                fieldLengthStatistics.Remove(field.Name);
            }
        }
    }

    private static IndexedDocument CreateIndexedDocument(BookmarkSearchDocument document)
    {
        UrlSearchTokens urlTokens = UrlSearchTokenizer.Tokenize(document.Url);
        Dictionary<IndexedFieldName, IndexedField> fields = [];

        AddField(fields, IndexedFieldName.Title, SearchTokenizer.Tokenize(document.Title));
        AddField(fields, IndexedFieldName.UrlHost, urlTokens.UrlHost);
        AddField(fields, IndexedFieldName.UrlDomainParts, urlTokens.UrlDomainParts);
        AddField(fields, IndexedFieldName.UrlPathParts, urlTokens.UrlPathParts);
        AddField(fields, IndexedFieldName.UrlText, urlTokens.UrlText);
        AddField(fields, IndexedFieldName.Tags, TokenizeMany(document.Tags ?? []));
        AddField(fields, IndexedFieldName.Notes, SearchTokenizer.Tokenize(document.Notes));

        return new IndexedDocument(
            document.Id,
            SearchTextNormalizer.Normalize(document.Title),
            SearchTextNormalizer.Normalize(document.Url),
            NormalizeFieldText(urlTokens.UrlHost),
            fields);
    }

    private static void AddField(
        Dictionary<IndexedFieldName, IndexedField> fields,
        IndexedFieldName fieldName,
        IReadOnlyList<SearchToken> tokens)
    {
        if (tokens.Count > 0)
        {
            fields[fieldName] = new IndexedField(fieldName, tokens);
        }
    }

    private static List<SearchToken> TokenizeMany(IEnumerable<string> values)
    {
        List<SearchToken> tokens = [];
        int position = 0;

        foreach (string value in values)
        {
            foreach (SearchToken token in SearchTokenizer.Tokenize(value))
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

    private static string NormalizeFieldText(IReadOnlyList<SearchToken> tokens)
    {
        return string.Join(' ', tokens.Select(token => token.Value));
    }

    private void ApplyQualityBonuses(
        IEnumerable<CandidateResult> candidates,
        IReadOnlyList<SearchToken> queryTokens,
        string query,
        bool includeDiagnostics)
    {
        string normalizedQuery = SearchTextNormalizer.Normalize(query);

        foreach (CandidateResult candidate in candidates)
        {
            IndexedDocument document = _indexedDocumentsById[candidate.Id];
            ApplyCoverageBonus(candidate, queryTokens.Count, includeDiagnostics);
            ApplyPhraseBonuses(candidate, document, normalizedQuery, includeDiagnostics);
            ApplyOrderAndProximityBonuses(candidate, document, queryTokens, includeDiagnostics);
            ApplyHostBonuses(candidate, document, normalizedQuery, queryTokens, includeDiagnostics);
        }
    }

    private static void ApplyCoverageBonus(
        CandidateResult candidate,
        int queryTokenCount,
        bool includeDiagnostics)
    {
        if (queryTokenCount == 0 || candidate.MatchedQueryTokenIndexes.Count == 0)
        {
            return;
        }

        if (candidate.MatchedQueryTokenIndexes.Count == queryTokenCount)
        {
            candidate.Score *= SearchScoringParameters.FullCoverageMultiplier;
            AddCoverageDiagnostics(candidate, queryTokenCount, includeDiagnostics);

            if (includeDiagnostics)
            {
                candidate.Diagnostics.AddBonus("coverage full");
            }

            return;
        }

        if (candidate.MatchedQueryTokenIndexes.Count * 2 >= queryTokenCount)
        {
            candidate.Score *= SearchScoringParameters.PartialCoverageMultiplier;
            AddCoverageDiagnostics(candidate, queryTokenCount, includeDiagnostics);

            if (includeDiagnostics)
            {
                candidate.Diagnostics.AddBonus("coverage partial");
            }
        }
        else
        {
            AddCoverageDiagnostics(candidate, queryTokenCount, includeDiagnostics);
        }
    }

    private static void AddCoverageDiagnostics(
        CandidateResult candidate,
        int queryTokenCount,
        bool includeDiagnostics)
    {
        if (includeDiagnostics)
        {
            candidate.Diagnostics.AddCoverage(candidate.MatchedQueryTokenIndexes.Count, queryTokenCount);
        }
    }

    private static void ApplyPhraseBonuses(
        CandidateResult candidate,
        IndexedDocument document,
        string normalizedQuery,
        bool includeDiagnostics)
    {
        if (normalizedQuery.Length == 0 || !normalizedQuery.Contains(' ', StringComparison.Ordinal))
        {
            return;
        }

        if (ContainsPhrase(document.NormalizedTitle, normalizedQuery))
        {
            candidate.Score += SearchScoringParameters.TitlePhraseBonus;

            if (includeDiagnostics)
            {
                candidate.Diagnostics.AddBonus("phrase title");
            }
        }

        if (ContainsPhrase(document.NormalizedUrl, normalizedQuery)
            || ContainsPhrase(document.NormalizedHostText, normalizedQuery))
        {
            candidate.Score += SearchScoringParameters.UrlPhraseBonus;

            if (includeDiagnostics)
            {
                candidate.Diagnostics.AddBonus("phrase url");
            }
        }
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        return text.Contains(phrase, StringComparison.Ordinal);
    }

    private static void ApplyOrderAndProximityBonuses(
        CandidateResult candidate,
        IndexedDocument document,
        IReadOnlyList<SearchToken> queryTokens,
        bool includeDiagnostics)
    {
        if (queryTokens.Count < 2)
        {
            return;
        }

        foreach (IndexedField field in document.Fields.Values)
        {
            int orderedMatches = CountOrderedQueryMatches(field.Tokens, queryTokens);

            if (orderedMatches == queryTokens.Count)
            {
                candidate.Score += SearchScoringParameters.OrderedTokenBonus;

                if (includeDiagnostics)
                {
                    candidate.Diagnostics.AddBonus($"ordered {field.Name.ToResultFieldName()}");
                }
            }

            if (HasAdjacentQueryMatches(field.Tokens, queryTokens))
            {
                candidate.Score += SearchScoringParameters.AdjacentTokenBonus;

                if (includeDiagnostics)
                {
                    candidate.Diagnostics.AddBonus($"adjacent {field.Name.ToResultFieldName()}");
                }
            }
        }
    }

    private static int CountOrderedQueryMatches(
        IReadOnlyList<SearchToken> fieldTokens,
        IReadOnlyList<SearchToken> queryTokens)
    {
        int queryIndex = 0;

        foreach (SearchToken fieldToken in fieldTokens.OrderBy(token => token.Position))
        {
            if (TokenMatchesQuery(fieldToken.Value, queryTokens[queryIndex].Value))
            {
                queryIndex++;

                if (queryIndex == queryTokens.Count)
                {
                    return queryIndex;
                }
            }
        }

        return queryIndex;
    }

    private static bool HasAdjacentQueryMatches(
        IReadOnlyList<SearchToken> fieldTokens,
        IReadOnlyList<SearchToken> queryTokens)
    {
        if (queryTokens.Count < 2)
        {
            return false;
        }

        SearchToken[] orderedFieldTokens = fieldTokens.OrderBy(token => token.Position).ToArray();

        for (int start = 0; start <= orderedFieldTokens.Length - queryTokens.Count; start++)
        {
            bool allMatch = true;

            for (int queryIndex = 0; queryIndex < queryTokens.Count; queryIndex++)
            {
                if (orderedFieldTokens[start + queryIndex].Position != orderedFieldTokens[start].Position + queryIndex
                    || !TokenMatchesQuery(orderedFieldTokens[start + queryIndex].Value, queryTokens[queryIndex].Value))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TokenMatchesQuery(string fieldToken, string queryToken)
    {
        return fieldToken == queryToken || fieldToken.StartsWith(queryToken, StringComparison.Ordinal);
    }

    private static void ApplyHostBonuses(
        CandidateResult candidate,
        IndexedDocument document,
        string normalizedQuery,
        IReadOnlyList<SearchToken> queryTokens,
        bool includeDiagnostics)
    {
        if (document.NormalizedHostText.Length == 0)
        {
            return;
        }

        string[] hostTokens = document.NormalizedHostText.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (normalizedQuery.Length > 0
            && hostTokens.Contains(
                normalizedQuery,
                StringComparer.Ordinal))
        {
            candidate.Score += SearchScoringParameters.ExactHostQueryBonus;

            if (includeDiagnostics)
            {
                candidate.Diagnostics.AddBonus("host exact");
            }
        }

        foreach (SearchToken queryToken in queryTokens)
        {
            if (hostTokens.Any(host => host.StartsWith(queryToken.Value, StringComparison.Ordinal)))
            {
                candidate.Score += SearchScoringParameters.HostStartsWithQueryBonus;

                if (includeDiagnostics)
                {
                    candidate.Diagnostics.AddBonus("host prefix");
                }

                return;
            }
        }
    }

    private double GetAverageFieldLength(IndexedFieldName field)
    {
        return _fieldLengthStatistics.TryGetValue(field, out FieldLengthStatistics? statistics)
            ? statistics.Average
            : 1.0;
    }

    private int GetFieldLength(string documentId, IndexedFieldName field)
    {
        if (_indexedDocumentsById.TryGetValue(documentId, out IndexedDocument? document)
            && document.Fields.TryGetValue(field, out IndexedField? indexedField))
        {
            return indexedField.Tokens.Count;
        }

        return 0;
    }

    private static BookmarkSearchDocument NormalizeDocument(BookmarkSearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new ArgumentException("Document ID must not be empty.", nameof(document));
        }

        string title = document.Title ?? string.Empty;
        string url = document.Url ?? string.Empty;
        string? notes = document.Notes;

        IReadOnlyList<string>? tags = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag)
            .ToArray();

        return document with
        {
            Title = title,
            Url = url,
            Tags = tags is { Count: > 0 } ? tags : null,
            Notes = notes
        };
    }

    private static void ValidateOptions(BookmarkSearchOptions options)
    {
        if (options.MaxResults < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxResults,
                "Maximum result count must not be negative.");
        }

        if (double.IsNaN(options.MinimumScore)
            || double.IsInfinity(options.MinimumScore)
            || options.MinimumScore < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MinimumScore,
                "Minimum score must be a finite non-negative number.");
        }
    }

    private sealed class CandidateResult(string id)
    {
        public string Id { get; } = id;

        public double Score { get; set; }

        public SortedSet<IndexedFieldName> MatchedFields { get; } = [];

        public HashSet<int> MatchedQueryTokenIndexes { get; } = [];

        public SearchDiagnosticsBuilder Diagnostics { get; } = new();
    }

    private sealed class FieldLengthStatistics
    {
        private int _totalLength;

        private int _documentsWithField;

        public bool IsEmpty => _documentsWithField == 0;

        public double Average => _documentsWithField == 0 ? 1.0 : (double)_totalLength / _documentsWithField;

        public void Add(int length)
        {
            _totalLength += length;
            _documentsWithField++;
        }

        public void Remove(int length)
        {
            _totalLength -= length;
            _documentsWithField--;
        }
    }
}
