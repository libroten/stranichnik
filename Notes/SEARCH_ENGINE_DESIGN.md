# Search Engine Design Task

This document is a self-contained development brief for an agent that will design and implement a C# search library for a bookmark manager.

The agent may not know anything about the Stranichnik project history. Treat this file as the source of product requirements, architectural constraints, and the recommended first implementation approach.

Important: this is a working design, not an irreversible contract. If implementation uncovers a simpler, safer, or clearly better approach, document the tradeoff before changing direction.

Companion documents:

- `Notes/SEARCH_ENGINE_ARCHITECTURE.md` contains the current technical architecture.
- `Notes/SEARCH_ENGINE_IMPLEMENTATION_PLAN.md` contains the step-by-step implementation plan for future agents.
- `Notes/SEARCH_ENGINE_INTEGRATION_GUIDE.md` contains the handoff guide for integrating the finished standalone library into the app.

## Product Goal

Build a local bookmark search engine that is meaningfully better than simple substring search.

The target user experience:

- The user types an approximate phrase, word, domain, or rough memory of a bookmark.
- The search returns the most relevant bookmark results.
- The query does not need to exactly match the bookmark title or URL.
- Typos, partial words, changed word order, and rough wording should still produce useful results when possible.

The first consumer will be a desktop bookmark manager, but the search engine itself should be implemented as an independent C# library, not as UI code and not as a storage-specific component.

## Why A Separate Library

Search is large enough to become its own project. It should not be tangled with the bookmark manager UI, SQLite storage, localization, synchronization, or encryption code.

The search library should:

- Be reusable outside the current application.
- Accept plain bookmark search documents from the caller.
- Return ranked result IDs and metadata.
- Keep all search index state in memory for the first version.
- Avoid Avalonia, SQLite, WebDAV, app settings, or application logging dependencies.

The bookmark manager can later integrate the library by reading bookmarks from its storage layer, converting them into search documents, and passing them into the index.

## Hard Requirements

### Functional Requirements

- Search only bookmarks. Folder search is not required.
- Index at least bookmark title and URL.
- Treat URL as structured data, not just one long string.
- Support English and Russian text at a practical level.
- Support approximate matching:
  - exact token matches
  - prefix matches
  - typo-tolerant/fuzzy matches
  - multi-word queries
  - rough word order matching
  - domain/path matching for URLs
- Rank results by relevance.
- Return deterministic results for the same index and query.
- Support rebuilding the full index.
- Support incremental add/update/remove operations.
- Handle empty or whitespace-only queries safely.
- Handle very long titles and very long URLs safely.
- Avoid throwing for malformed URLs. Malformed URLs should still be tokenized as plain text as well as possible.

### Privacy Requirements

The first version must be an in-memory index.

Do not write the index to disk.

Do not require or create persistent search files.

Do not log bookmark titles, URLs, tokens, query text, or indexed content by default.

Secret/encrypted bookmarks are a caller concern:

- While the app is locked, the caller should simply not pass secret bookmarks into the index.
- After unlock, the caller may decrypt secret bookmarks and add them to the in-memory index.
- On lock or app exit, the caller must be able to remove secret documents or clear/rebuild the index.

The search library must not decrypt anything and must not know the master password. It only receives plaintext documents that the caller has decided are currently searchable.

### Architecture Requirements

- Implement as a plain C#/.NET library.
- Prefer pure managed C# for the first implementation.
- Avoid native dependencies in the first version.
- Avoid server processes.
- Avoid network calls.
- Avoid UI framework dependencies.
- Keep public API small and stable.
- Keep ranking explainable enough for tests and future tuning.

Recommended target:

- `net8.0` is acceptable for the first version.
- If reuse becomes important, consider `netstandard2.1` later, but do not start there unless there is a concrete need.

## Non-Goals For The First Version

Do not implement these in the first version:

- UI search box or result list.
- SQLite FTS integration.
- Lucene.NET integration.
- Persistent index files.
- Semantic/vector search.
- ML model inference.
- Web page content crawling.
- Browser history search.
- Folder search.
- Tag management UI.
- Encrypted searchable index.
- Cross-device search index synchronization.
- Perfect morphology for Russian or English.

These can be revisited later after the first in-memory lexical/fuzzy search works well.

## Recommended Public API

The exact names can change, but the library should expose a small API shaped roughly like this:

```csharp
public sealed record BookmarkSearchDocument(
    string Id,
    string Title,
    string Url,
    IReadOnlyList<string>? Tags = null,
    string? Notes = null);

public sealed record BookmarkSearchOptions
{
    public int MaxResults { get; init; } = 50;
    public double MinimumScore { get; init; } = 0.0;
    public bool IncludeDiagnostics { get; init; } = false;
}

public sealed record BookmarkSearchResult(
    string Id,
    double Score,
    IReadOnlyList<string> MatchedFields,
    string? Diagnostics = null);

public interface IBookmarkSearchIndex
{
    void Rebuild(IEnumerable<BookmarkSearchDocument> documents);
    void AddOrUpdate(BookmarkSearchDocument document);
    bool Remove(string id);
    void Clear();
    IReadOnlyList<BookmarkSearchResult> Search(
        string query,
        BookmarkSearchOptions? options = null);
}
```

Design notes:

- Use string IDs because they integrate easily with SQLite IDs, sync IDs, and UI IDs.
- Do not return full document content by default. Return IDs and ranking metadata. The caller can map IDs back to bookmarks.
- `Tags` and `Notes` are optional future-facing fields. They do not need UI support in the first consuming app.
- `Diagnostics` can be useful in tests and tuning, but should be opt-in.

## Internal Architecture

Recommended internal components:

- `SearchTextNormalizer`
- `SearchTokenizer`
- `UrlSearchTokenizer`
- `InMemoryBookmarkSearchIndex`
- `InvertedIndex`
- `FuzzyTermIndex`
- `SearchScorer`
- `SearchQueryParser`

Keep these components UI-independent and testable.

## Text Normalization

Normalize both documents and queries consistently.

Minimum recommended normalization:

- Unicode normalization, preferably Form C or Form KC after testing.
- Lowercase using invariant culture or a deliberately chosen culture-insensitive approach.
- Trim whitespace.
- Collapse repeated whitespace.
- Treat punctuation as token separators where appropriate.
- Preserve letters and digits.
- Normalize Russian `ё` to `е` for practical search.
- Handle mixed Russian/English text.

Do not over-normalize URLs so aggressively that useful domain/path information is lost.

## Tokenization

Tokenize by field. Field-aware scoring is important.

Recommended indexed fields:

- `title`
- `url`
- `url_host`
- `url_domain_parts`
- `url_path_parts`
- optional future `tags`
- optional future `notes`

For normal text:

- Split on whitespace and punctuation.
- Keep alphanumeric tokens.
- Ignore very short tokens only carefully. One-letter tokens can matter in technology names like `C#`, `F#`, or `R`.
- Consider mapping `c#` to both `csharp` and `c` if tests show it helps.

For URLs:

- Try to parse with `Uri.TryCreate`.
- Extract host/domain tokens.
- Split host by dots and hyphens.
- Split path by slashes, hyphens, underscores, dots, and query separators.
- Decode percent-encoded path parts where safe.
- Index the original URL as fallback text.
- Index hostname without leading `www`.

Example URL tokens for `https://learn.microsoft.com/dotnet/csharp/`:

- `learn.microsoft.com`
- `learn`
- `microsoft`
- `com`
- `dotnet`
- `csharp`

## Stemming And Morphology

Do not make perfect morphology a first-version requirement.

The first version should be designed so token filters can be added later.

Recommended interface direction:

```csharp
public interface ITokenFilter
{
    IEnumerable<string> Apply(string token);
}
```

Possible first-version filters:

- identity token
- lowercase/normalized token
- very small English suffix simplification only if backed by tests
- very small Russian suffix simplification only if backed by tests

Be careful: naive stemming can make results worse. If a rule is not clearly useful in tests, leave it out.

Good first version is allowed to rely on exact/prefix/fuzzy matching instead of heavy morphology.

## Indexing Strategy

Use an in-memory inverted index.

Recommended data structures:

- Document map: `documentId -> indexed document metadata`.
- Term dictionary: normalized term -> term metadata.
- Field-aware postings: `term -> list of (documentId, field, termFrequency, positions)`.
- Document length per field for BM25-style scoring.
- Trigram/k-gram index: `gram -> candidate terms` for fuzzy lookup.

The first version can keep structures simple and optimize later.

Expected scale:

- A bookmark manager will likely have thousands to tens of thousands of bookmarks.
- The design should not collapse at 50,000-100,000 bookmarks.
- It does not need to be a web-scale search engine.

## Query Processing

Recommended query flow:

1. Normalize query.
2. Tokenize query.
3. If query is empty, return no results.
4. For each query token:
   - find exact term matches
   - find prefix term matches
   - find fuzzy candidate terms through trigram/k-gram overlap
   - apply edit distance to prune bad fuzzy candidates
5. Collect candidate documents from matching terms.
6. Score candidates.
7. Apply minimum score threshold.
8. Sort by score descending.
9. Use deterministic tie-breakers, such as title then ID.
10. Return at most `MaxResults`.

## Fuzzy Matching

Use fuzzy matching as candidate expansion, not as the only ranking method.

Recommended approach:

- Build a trigram or k-gram index over indexed terms.
- For a query token, generate its grams.
- Retrieve candidate terms sharing enough grams.
- Compute edit distance only for those candidate terms.
- Penalize fuzzy matches by edit distance and token length.

Avoid comparing every query token against every indexed token with edit distance. That can become slow as the corpus grows.

Initial fuzzy thresholds can be simple:

- For token length 1-2: exact or prefix only.
- For token length 3-5: allow edit distance 1.
- For token length 6+: allow edit distance 2.

Tune this with tests rather than guessing forever.

## Ranking Strategy

Use a hybrid lexical ranking model.

Recommended scoring ingredients:

- BM25-style score for exact token matches.
- Field weights.
- Prefix match bonus.
- Fuzzy match score with edit-distance penalty.
- Phrase/exact substring bonus.
- Query coverage bonus.
- Proximity/order bonus for multi-word queries.
- URL/domain-specific boosts.

Suggested initial field weights:

- Title: high weight.
- URL host/domain: high weight.
- URL path: medium weight.
- Full URL fallback text: low weight.
- Tags/notes later: tune separately.

Suggested relative behavior:

- Exact title token match should beat URL-only token match.
- Exact domain match should be strong.
- Prefix match should be weaker than exact match but stronger than fuzzy typo match.
- A result matching all query tokens should usually beat a result matching only one token.
- A phrase in title should usually rank very high.

Do not hardcode magic numbers everywhere. Keep scoring constants in one place so they are easy to tune.

## URL-Specific Ranking

Bookmarks are not generic documents. URL search matters.

Important cases:

- Query `github` should strongly match `https://github.com/`.
- Query `dotnet docs` should match a title like `.NET Documentation` and URL path `/dotnet/`.
- Query `avalonia` should match both title and domain/path.
- Query `learn microsoft` should match `learn.microsoft.com`.
- Query `csharp` should match `csharp`, `c-sharp`, and possibly `C#` if token filters support it.

Domain and host matches should receive specific boosts because users often remember the site more clearly than the full title.

## Diagnostics And Explainability

The library should be testable and tuneable.

When `IncludeDiagnostics = true`, results may include a short explanation such as:

- matched title token `avalonia`
- matched URL host token `github`
- fuzzy matched `avalnoia -> avalonia`
- phrase bonus applied
- query coverage `2/2`

Diagnostics should be concise and intended for tests/development, not end-user UI.

## Performance Expectations

Set modest but explicit goals.

For a development machine and a corpus up to tens of thousands of bookmarks:

- Rebuilding the index should be acceptable at app startup.
- Incremental add/update/remove should be fast enough for interactive CRUD.
- Typical searches should feel instant.

Do not optimize prematurely, but design around candidate retrieval rather than full corpus scoring.

Recommended tests/benchmarks later:

- 1,000 documents
- 10,000 documents
- 50,000 documents
- long URL stress case
- repeated incremental updates

## Error Handling

The search library should be forgiving.

- Null documents should be rejected clearly.
- Empty IDs should be rejected clearly.
- Duplicate IDs in `Rebuild` should have defined behavior. Prefer "last document wins" or throw consistently; document the choice.
- Invalid URLs should not fail indexing.
- Search should not throw for unusual Unicode input.
- Very long fields should not cause pathological behavior.

## Testing Requirements

Write tests before or alongside implementation.

Core unit test areas:

- text normalization
- Russian `ё`/`е` behavior
- tokenizer behavior
- URL token extraction
- exact token search
- prefix search
- typo/fuzzy search
- multi-word query ranking
- title-vs-url field weighting
- domain ranking
- add/update/remove index operations
- empty query
- malformed URL
- very long title and URL
- deterministic ordering

Ranking tests should use a small fixed corpus and assert relative ordering.

Example ranking assertions:

- Query `avalonia docs` ranks `Avalonia Docs` above unrelated bookmarks.
- Query `avlaonia` still finds `Avalonia Docs`.
- Query `github` ranks a GitHub bookmark high because of host/domain match.
- Query `dotnet csharp` finds `.NET`/`C#` documentation.
- Query with all terms matched ranks above a result with only one term matched.
- Updating a document removes old tokens from the index.
- Removing a document prevents it from appearing in future results.

Avoid brittle tests that depend on exact floating point scores unless the scoring formula is deliberately frozen.

Prefer:

- result contains expected ID
- expected ID ranks above another ID
- result count is correct
- removed ID is absent

## Recommended Implementation Phases

### Phase 1: Library Skeleton

- Create a standalone C# library project.
- Create a test project.
- Define public records and `IBookmarkSearchIndex`.
- Add a simple in-memory implementation shell.

### Phase 2: Normalization And Tokenization

- Implement normalizer.
- Implement text tokenizer.
- Implement URL tokenizer.
- Add focused tests.

### Phase 3: Exact Search

- Build document map.
- Build field-aware inverted index.
- Implement exact token search.
- Add simple field-weighted scoring.

### Phase 4: BM25-Style Ranking

- Add document length statistics.
- Add BM25-style scoring per field.
- Add query coverage and deterministic sorting.
- Add relative ranking tests.

### Phase 5: Prefix Search

- Add prefix candidate lookup.
- Keep prefix score below exact score.
- Add tests for partial remembered words.

### Phase 6: Fuzzy Search

- Add trigram/k-gram term index.
- Add edit-distance pruning.
- Add fuzzy scoring penalties.
- Add typo tests.

### Phase 7: Multi-Word Quality

- Add phrase/proximity/order bonuses.
- Improve query coverage handling.
- Add tests for rough remembered phrases.

### Phase 8: API Polish

- Add XML documentation for public types.
- Add clear README usage example for the library.
- Add diagnostics/explain mode if not already done.

### Phase 9: Integration Notes For Bookmark Manager

- Document how a host app should build `BookmarkSearchDocument` objects.
- Document how to handle secret bookmarks by excluding or clearing them.
- Document how CRUD operations map to `AddOrUpdate` and `Remove`.

## Integration Guidance For Stranichnik-Like Apps

The consuming bookmark manager should integrate the library roughly like this:

1. Load bookmarks from storage.
2. Convert searchable bookmarks into `BookmarkSearchDocument`.
3. Call `Rebuild`.
4. On add/edit bookmark, call `AddOrUpdate`.
5. On delete bookmark, call `Remove`.
6. On unlock secrets, add decrypted secret bookmarks.
7. On lock secrets, rebuild from non-secret bookmarks or remove secret IDs.
8. Keep UI result rendering outside the search library.

Moving a bookmark between folders does not necessarily require search index update unless folder path/context is indexed later.

## Future Options To Keep Open

The first version should not implement these, but it should not make them impossible:

- SQLite FTS5 for persistent non-secret index.
- Lucene.NET if search quality requirements grow.
- Semantic/vector search for optional "meaning" search.
- Highlight spans for UI.
- Search by tags or notes.
- Search filters, such as domain, date, secret/non-secret, or folder.
- Separate normal and secret indexes.
- Background index rebuild.

## External References For Future Research

These are useful reference directions, not mandatory dependencies:

- SQLite FTS5: https://www.sqlite.org/fts5.html
- Lucene.NET: https://www.nuget.org/packages/Lucene.Net/absoluteLatest
- Lucene.NET FuzzyQuery: https://lucenenet.apache.org/docs/4.8.0-beta00017/api/core/Lucene.Net.Search.FuzzyQuery.html
- Lucene.NET RussianAnalyzer: https://lucenenet.apache.org/docs/4.8.0-beta00012/api/analysis-common/Lucene.Net.Analysis.Ru.html
- Stanford IR book, BM25: https://nlp.stanford.edu/IR-book/html/htmledition/okapi-bm25-a-non-binary-model-1.html
- Stanford IR book, k-gram spelling correction: https://nlp.stanford.edu/IR-book/html/htmledition/k-gram-indexes-for-spelling-correction-1.html
- Meilisearch ranking concepts: https://www.meilisearch.com/docs/capabilities/full_text_search/relevancy/ranking_rules
- Typesense ranking concepts: https://typesense.org/docs/guide/ranking-and-relevance.html

## Acceptance Criteria

The library is ready for first integration when:

- It builds as an independent C# library.
- It has meaningful unit tests for normalization, tokenization, indexing, exact search, prefix search, fuzzy search, ranking, and update/remove behavior.
- It can search a small bookmark corpus better than substring matching.
- It returns deterministic ranked results.
- It does not write index data to disk.
- It does not depend on Avalonia, SQLite, WebDAV, or Stranichnik view models.
- Its public API is small enough to integrate into a desktop app without exposing internal scoring/indexing details.
