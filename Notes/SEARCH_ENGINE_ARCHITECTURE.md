# Search Engine Architecture

This document describes the proposed architecture for the standalone C# bookmark
search library. It expands `Notes/SEARCH_ENGINE_DESIGN.md` into concrete
technical decisions that should guide implementation.

The design is intentionally independent from the current Stranichnik Avalonia
application, SQLite storage, localization, logging, encryption, and sync code.

The step-by-step implementation plan for future agents lives in
`Notes/SEARCH_ENGINE_IMPLEMENTATION_PLAN.md`.

The application integration handoff guide lives in
`Notes/SEARCH_ENGINE_INTEGRATION_GUIDE.md`.

## Goals

The library should provide local bookmark search that is substantially better
than simple substring matching:

- exact token search;
- prefix search;
- typo-tolerant fuzzy search;
- multi-word queries;
- rough word order matching;
- field-aware relevance ranking;
- URL-aware domain, host, and path matching;
- practical English and Russian token handling;
- deterministic result ordering.

The first version keeps the whole index in memory. It must not write index data
to disk or require any persistent search files.

## Project Shape

Recommended project:

- directory: `Stranichnik.Search`;
- project file: `Stranichnik.Search/Stranichnik.Search.csproj`;
- target framework: `net8.0`;
- output type: plain class library;
- implementation language: C#;
- dependency direction: no dependency on the existing Stranichnik app project.

The project should be usable by any caller that can provide bookmark-like
documents. It should not reference:

- Avalonia;
- SQLite;
- app settings;
- app logging;
- encryption services;
- sync services;
- view models;
- storage records from the main app.

If a test project is added later, it should also be separate from the existing
application tests unless there is a deliberate solution-level decision to share
test infrastructure.

For a newly created `.csproj`, include the built-in .NET analyzers:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <AnalysisLevel>latest</AnalysisLevel>
  <AnalysisMode>AllEnabledByDefault</AnalysisMode>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

## Public API

The public API should stay small. The caller should pass plain documents into
an index and receive ranked document IDs back.

Proposed public types:

```csharp
namespace Stranichnik.Search;

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

Recommended concrete implementation:

```csharp
public sealed class InMemoryBookmarkSearchIndex : IBookmarkSearchIndex
```

The caller should keep ownership of full bookmark data. Search results return
IDs and ranking metadata only. This avoids leaking titles, URLs, notes, or tags
through result objects unless the caller explicitly maps IDs back to documents.

`BookmarkSearchDocument.Url` intentionally remains a string rather than
`System.Uri`. The library must accept malformed URLs, schemeless host/path text,
and other URL-like user data without throwing. URL parsing is a best-effort
internal indexing step, not a public API validation boundary.

## API Validation Rules

`BookmarkSearchDocument` data is user-provided content, so the index should be
strict about document identity but forgiving about searchable text.

Recommended behavior:

- `Rebuild(null)` throws `ArgumentNullException`.
- `AddOrUpdate(null)` throws `ArgumentNullException`.
- document `Id` must be non-null, non-empty, and non-whitespace.
- `Title`, `Url`, `Tags`, and `Notes` may contain empty or unusual text.
- even though public records use nullable annotations, callers can still pass
  runtime nulls; treat null `Title`, `Url`, and `Notes` as empty strings.
- treat null `Tags` as an empty tag list.
- ignore null, empty, or whitespace-only tag values.
- duplicate IDs during `Rebuild` use "last document wins".
- malformed URLs never fail indexing.
- empty or whitespace-only queries return an empty result list.
- `MaxResults < 0` should be rejected with `ArgumentOutOfRangeException`.
- `MaxResults == 0` should return no results.
- `MinimumScore` must be finite and greater than or equal to zero.
- `MinimumScore < 0`, `double.NaN`, `double.PositiveInfinity`, and
  `double.NegativeInfinity` should be rejected with
  `ArgumentOutOfRangeException`.

"Last document wins" makes `Rebuild` consistent with repeated `AddOrUpdate`
calls and simplifies integration with callers that project data from storage.

## Internal Components

Recommended internal namespace:

```csharp
namespace Stranichnik.Search.Internal;
```

Main components:

- `SearchTextNormalizer`;
- `SearchTokenizer`;
- `UrlSearchTokenizer`;
- `IndexedDocument`;
- `IndexedField`;
- `InvertedIndex`;
- `KGramTermIndex`;
- `EditDistance`;
- `SearchScorer`;
- `SearchDiagnosticsBuilder`.

Current first-version note:

- there is no separate `SearchQueryParser`; queries are normalized and tokenized
  directly with `SearchTokenizer`;
- there is no separate `TermDictionary`; `InvertedIndex` owns postings and keeps
  a sorted in-memory term set for prefix expansion;
- these can still be extracted later if query parsing or term lookup grows more
  complex.

Public API types should not expose internal indexing structures. This keeps the
library replaceable later if a different implementation is needed.

## Indexed Fields

The index should be field-aware because bookmark search depends heavily on
where a match occurred.

Initial fields:

| Field | Purpose | Weight |
| --- | --- | --- |
| `title` | bookmark title text | high |
| `url_host` | full normalized host, such as `learn.microsoft.com` | high |
| `url_domain_parts` | host pieces, such as `learn`, `microsoft`, `com` | high |
| `url_path_parts` | URL path, query, and fragment pieces | medium |
| `url_text` | fallback tokenization of the whole URL string | low |
| `tags` | optional future-facing metadata | medium-high |
| `notes` | optional future-facing metadata | low-medium |

The first consuming application may not expose tags or notes yet, but keeping
the public document shape ready for them avoids a near-future API break.

If a caller provides `Tags` or `Notes`, version one should index them. The host
application may pass null or empty values until it has UI/storage support for
those fields, but the library should not silently ignore populated data that is
already part of the public document contract.

## Text Normalization

Normalization must be identical for documents and queries.

Recommended rules:

1. Convert `null` text inputs to empty strings at indexing boundaries.
2. Apply Unicode normalization Form KC.
3. Convert to lowercase with invariant culture.
4. Normalize Russian `ё` to `е`.
5. Treat punctuation and symbols as token separators, except where token
   filters intentionally preserve useful variants.
6. Preserve letters and digits.
7. Collapse repeated whitespace.
8. Trim leading and trailing whitespace.

Form KC is preferred for practical search because it folds many compatibility
characters into searchable equivalents. This is useful for user-entered text
and copied titles. If tests uncover harmful behavior, Form C can be substituted
before implementation is frozen.

## Tokenization

Text tokenization should produce simple normalized tokens and token positions.
Positions are needed for phrase and proximity scoring.

Recommended token representation:

```csharp
internal readonly record struct SearchToken(
    string Value,
    int Position);
```

Normal text tokenization:

- split on whitespace, punctuation, and symbols;
- keep Unicode letters and digits;
- keep one-letter tokens initially because `R`, `C`, and similar names can be
  meaningful;
- cap token length to a safe maximum for indexing, for example 128 characters;
- preserve token positions after filtering.

Token filters should be deliberately small in the first version:

- identity token;
- Russian `ё` to `е` through the normalizer;
- aliases for common programming tokens when present in source text:
  - `c#` -> `csharp`, `c`;
  - `f#` -> `fsharp`, `f`;
  - `.net` -> `dotnet`, `net`.

Avoid broad stemming in version one. Naive Russian or English suffix stripping
can hurt relevance and should only be added later with tests proving benefit.

## URL Tokenization

URLs should be treated as structured data first and plain text second.

Recommended flow:

1. Keep the original URL string as fallback input for `url_text`.
2. Try to parse with `Uri.TryCreate(url, UriKind.Absolute, out var uri)`.
3. If absolute parsing fails but the input looks like a domain or host/path
   without a scheme, try a best-effort parse with an added `https://` prefix.
   This is only for token extraction; do not rewrite or return the URL.
4. If parsing succeeds:
   - extract `Host`;
   - lowercase and normalize host;
   - remove a leading `www.`;
   - index the full host as `url_host`;
   - for internationalized domains, index both the raw normalized host and a
     best-effort Unicode/punycode alternate when .NET `Uri` APIs expose one
     safely;
   - split host by `.`, `-`, and `_` into `url_domain_parts`;
   - extract path, query, and fragment;
   - percent-decode path-like segments when safe;
   - split path/query/fragment by `/`, `\`, `.`, `-`, `_`, `=`, `&`, `?`, `#`;
   - tokenize these pieces as `url_path_parts`.
5. If parsing fails:
   - tokenize the full URL-like string as `url_text`;
   - additionally apply simple host-looking splitting around `.`, `/`, `-`,
     and `_` so malformed URLs remain searchable.

The tokenizer should never throw for malformed escapes, unusual characters,
IDN/punycode edge cases, or invalid URI syntax. Unsafe percent-decoding should
fall back to the raw segment.

Examples:

- `https://learn.microsoft.com/dotnet/csharp/`
  - `url_host`: `learn.microsoft.com`
  - `url_domain_parts`: `learn`, `microsoft`, `com`
  - `url_path_parts`: `dotnet`, `csharp`
- `https://github.com/AvaloniaUI/Avalonia`
  - `url_host`: `github.com`
  - `url_domain_parts`: `github`, `com`
  - `url_path_parts`: `avaloniaui`, `avalonia`

## Index Data Structures

The first implementation should use straightforward in-memory structures:

```csharp
private readonly Dictionary<string, IndexedDocument> _documentsById;
private readonly InvertedIndex _invertedIndex;
private readonly KGramTermIndex _kGramIndex;
```

`IndexedDocument` should contain:

- document ID;
- optional normalized title for deterministic sorting;
- indexed field lengths;
- indexed terms by field;
- compact source metadata needed for removal/update.

`InvertedIndex` should map terms to postings:

```csharp
term -> field -> documentId -> Posting
```

`Posting` should contain:

- document ID;
- field;
- term frequency;
- token positions.

The implementation can use dictionaries and lists first. If profiling later
shows memory pressure, postings can be compacted into sorted arrays.

## Rebuild And Incremental Updates

`Rebuild` should:

1. clear all internal structures;
2. validate documents;
3. index each document in input order;
4. for duplicate IDs, replace the previous indexed document;
5. rebuild document frequency and average field length statistics.

`AddOrUpdate` should:

1. validate the document;
2. remove the old version if the ID already exists;
3. index the new version;
4. update term dictionary, postings, k-gram data, and field statistics.

`Remove` should:

1. return `false` if the ID is unknown;
2. remove document postings from all affected terms;
3. remove empty term entries;
4. update field statistics;
5. return `true`.

For version one, updating average field lengths by recalculating totals from
maintained counters is sufficient. Full rebuilds are acceptable when the caller
explicitly requests them, but ordinary `AddOrUpdate` and `Remove` should not
rebuild the entire index.

## Query Processing

Search flow:

1. Validate options.
2. Normalize query.
3. Tokenize query.
4. Return an empty result list when no query tokens remain.
5. For each query token, collect term matches:
   - exact;
   - prefix;
   - fuzzy through k-gram candidates and edit distance.
6. Collect candidate document IDs from matched terms.
7. Score only candidate documents.
8. Apply `MinimumScore`.
9. Sort deterministically.
10. Return at most `MaxResults`.

Query parsing should keep the initial version simple. Quoted phrases, boolean
operators, and field-specific query syntax are out of scope for version one.

## Prefix Matching

Prefix matching should support incomplete remembered words, for example:

- `aval` -> `avalonia`;
- `micro` -> `microsoft`;
- `dot` -> `dotnet`.

Initial implementation can scan the term dictionary for prefixes if the corpus
is small enough. However, the architecture should isolate prefix lookup behind
a component, for example:

```csharp
internal interface ITermCandidateSource
{
    IReadOnlyList<TermMatch> FindMatches(string queryToken);
}
```

This allows a later replacement with a trie or sorted-term binary range lookup.

Recommended version-one rule:

- length 1-2: exact only, unless matching host/domain parts;
- length 3+: allow prefix matches;
- cap prefix expansions per token to protect latency.

Prefix matches should score lower than exact matches and higher than fuzzy
matches.

## Fuzzy Matching

Fuzzy matching should expand candidate terms, not scan every indexed term with
edit distance.

Use a k-gram index:

- grams: trigrams for normal tokens;
- boundary markers may be added later if tests show better precision;
- token length below 3 should not use fuzzy matching.

Recommended candidate flow:

1. Generate grams for the query token.
2. Get indexed terms sharing grams.
3. Count gram overlap by candidate term.
4. Keep only candidates with enough overlap.
5. Compute bounded Levenshtein distance for remaining terms.
6. Discard candidates above the allowed distance.

Initial edit-distance thresholds:

| Query token length | Fuzzy behavior |
| --- | --- |
| 1-2 | exact only |
| 3-5 | edit distance <= 1 |
| 6+ | edit distance <= 2 |

Use a bounded edit-distance implementation that exits once the threshold is
exceeded. This prevents long tokens from causing unnecessary work.

Fuzzy score should be penalized by:

- edit distance;
- shorter token length;
- low gram overlap;
- low-value fields.

## Ranking Model

Use a hybrid lexical model:

- BM25-style scoring for exact/prefix/fuzzy term matches;
- field weights;
- match type multipliers;
- phrase bonus;
- proximity/order bonus;
- query coverage bonus;
- URL-specific boosts;
- deterministic tie-breakers.

BM25 is preferred over raw term frequency because it handles document length
better and is explainable in tests.

Recommended BM25 constants:

```text
k1 = 1.2
b = 0.75
```

BM25 formula per field:

```text
idf = ln(1 + (documentCount - documentFrequency + 0.5) / (documentFrequency + 0.5))
score = idf * ((tf * (k1 + 1)) / (tf + k1 * (1 - b + b * fieldLength / averageFieldLength)))
```

If a field has no average length yet, use `1` as a safe denominator.

Recommended initial field weights:

| Field | Weight |
| --- | ---: |
| `title` | 3.0 |
| `url_host` | 3.0 |
| `url_domain_parts` | 2.6 |
| `url_path_parts` | 1.8 |
| `tags` | 2.2 |
| `notes` | 1.1 |
| `url_text` | 0.7 |

Recommended match type multipliers:

| Match type | Multiplier |
| --- | ---: |
| exact | 1.0 |
| prefix | 0.72 |
| fuzzy distance 1 | 0.55 |
| fuzzy distance 2 | 0.35 |

These constants should live in one internal options/settings class, for example
`SearchScoringParameters`, so ranking can be tuned without scattering magic
numbers through the code.

## Ranking Bonuses

Additive bonuses should be small relative to strong exact field matches. They
should refine ordering rather than overpower the base score.

Recommended bonuses:

- query coverage:
  - all query tokens matched: multiply final score by `1.20`;
  - at least half matched: multiply by `1.05`;
- exact phrase in normalized title: add `2.0`;
- exact phrase in normalized URL/host: add `1.2`;
- ordered query tokens in same field with gaps: add up to `1.0`;
- adjacent query tokens in same field: add up to `1.5`;
- exact host/domain query match: add `1.5`;
- host starts with query token: add `0.8`.

The scorer should track which query tokens matched each document. This enables
coverage bonuses and useful diagnostics.

## Deterministic Ordering

After scoring:

1. sort by score descending;
2. tie-break by normalized title ascending;
3. tie-break by document ID ordinal ascending.

Never rely on dictionary enumeration order for final result ordering.

Floating-point comparisons should use the raw `double` values for sorting. Tests
should assert relative ordering rather than exact scores unless a specific
scoring formula is intentionally locked down.

## Diagnostics

Diagnostics are opt-in through `BookmarkSearchOptions.IncludeDiagnostics`.

When diagnostics are disabled:

- do not allocate large explanation strings;
- return `Diagnostics = null`.

When enabled, diagnostics may include compact fragments such as:

- `exact title: avalonia`;
- `prefix url_path_parts: dot -> dotnet`;
- `fuzzy title: avlaonia -> avalonia, distance 1`;
- `coverage: 2/2`;
- `phrase bonus: title`;
- `host boost: github.com`.

Diagnostics are for tests and tuning. They should not be treated as localized
end-user UI text.

Diagnostics may contain query terms, matched indexed tokens, field names, host
names, and fuzzy-match mappings. That makes them potentially sensitive. The
library must keep diagnostics disabled by default, must not log diagnostics, and
must only return them when the caller explicitly sets `IncludeDiagnostics =
true`. Host applications should avoid enabling diagnostics for normal user
searches, especially when secret bookmarks are indexed after unlock, unless the
caller deliberately accepts that exposure for development or troubleshooting.

## Privacy And Secret Bookmarks

The search library does not know whether a document is secret. It only indexes
the plaintext documents passed by the caller.

The host application must enforce privacy boundaries:

- while locked, do not pass secret bookmarks to `Rebuild`;
- after unlock, decrypt secret bookmarks outside the search library and call
  `AddOrUpdate`;
- on lock, call `Remove` for secret document IDs or rebuild the index with only
  non-secret documents;
- on app exit, discard the index instance.

The library must not:

- write index data to disk;
- log query text, bookmark titles, URLs, notes, tags, or tokens;
- decrypt data;
- depend on a master password or encryption service.

## Performance Expectations

The design target is bookmark-scale search:

- thousands to tens of thousands of bookmarks should feel instant for typical
  queries;
- 50,000-100,000 bookmarks should remain practical;
- indexing may take noticeable but acceptable time during startup or unlock;
- add/update/remove should be fast enough for interactive CRUD operations.

Important implementation constraints:

- score candidate documents only, not the whole corpus;
- never compute edit distance against every indexed term;
- cap prefix and fuzzy expansions per query token;
- cap indexed token length;
- avoid retaining duplicate strings when easy to avoid.

The first implementation can optimize only after correctness and ranking tests
exist. Candidate retrieval boundaries should still be designed from the start.

## Threading Model

Recommended first version: not thread-safe.

`InMemoryBookmarkSearchIndex` should assume all operations are called from one
logical owner. This keeps implementation and tests simpler.

The host must not call `Search` concurrently with `Rebuild`, `AddOrUpdate`,
`Remove`, or `Clear` on the same index instance. If the host later searches on a
background thread while UI or storage code mutates bookmarks, the host must add
its own synchronization or use immutable index snapshots.

Document this explicitly. If the host app needs concurrent reads and writes
later, add either:

- a synchronization wrapper around `IBookmarkSearchIndex`; or
- an immutable snapshot index with copy-on-write updates.

The initial desktop integration can call search and CRUD updates from the UI
coordination layer without exposing the index to arbitrary background mutation.

## Error Handling

The library should be predictable:

- programming errors such as null collections and empty IDs throw clear
  exceptions;
- user-content problems such as malformed URLs or unusual Unicode do not throw;
- search over an empty index returns an empty result list;
- removing an unknown ID returns `false`;
- clearing an already empty index is valid.

Very long title/URL inputs should be handled by token and field caps rather
than exceptions.

Recommended caps for version one:

- maximum token length indexed: 128 characters;
- maximum tokens per single field: 512;
- maximum prefix expansions per query token: 128;
- maximum fuzzy candidate terms after gram filtering per query token: 256.

Caps should be internal constants and covered by stress tests.

## Testing Strategy

A separate test project should be created when implementation begins.

Recommended test project:

- directory: `Stranichnik.Search.Tests`;
- project file: `Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj`;
- framework: xUnit, matching the existing repository preference;
- dependency: reference only `Stranichnik.Search`.

Core test areas:

- text normalization;
- Russian `ё`/`е` matching;
- English/Russian tokenization;
- programming-token aliases such as `C#`/`csharp`;
- URL host/domain/path extraction;
- malformed URL fallback tokenization;
- exact token search;
- prefix search;
- fuzzy typo search;
- title-vs-URL field weighting;
- domain ranking;
- multi-word query coverage;
- phrase/proximity ordering;
- add/update/remove semantics;
- duplicate ID behavior in `Rebuild`;
- empty query behavior;
- very long title and URL safety;
- deterministic ordering.

Ranking tests should use relative assertions:

- expected ID appears;
- expected ID ranks above another ID;
- removed ID is absent;
- result count is bounded by `MaxResults`;
- results are stable across repeated calls.

Avoid exact floating-point score assertions unless a formula is deliberately
frozen.

## Implementation Phases

### Phase 1: Skeleton

- Create `Stranichnik.Search` as a standalone class library.
- Add analyzer properties to the new project file.
- Add public records and `IBookmarkSearchIndex`.
- Add `InMemoryBookmarkSearchIndex` with empty operational structure.

### Phase 2: Normalization And Tokenization

- Implement `SearchTextNormalizer`.
- Implement `SearchTokenizer`.
- Implement `UrlSearchTokenizer`.
- Add focused tests for text and URL token extraction.

### Phase 3: Exact Search

- Add document map.
- Add field-aware inverted index.
- Add exact term candidate retrieval.
- Add simple field-weighted scoring.

### Phase 4: BM25 Ranking

- Track document frequencies and field lengths.
- Implement BM25-style scoring.
- Add deterministic ordering and coverage scoring.

### Phase 5: Prefix Search

- Add prefix term expansion behind a replaceable component.
- Score prefix matches below exact matches.
- Add partial-word tests.

### Phase 6: Fuzzy Search

- Add k-gram index.
- Add bounded edit distance.
- Add fuzzy candidate pruning and scoring.
- Add typo tests.

### Phase 7: Multi-Word Quality

- Add phrase, proximity, order, and coverage bonuses.
- Add URL/domain-specific boosts.
- Add diagnostics for ranking explanations.

### Phase 8: API Polish

- Add XML documentation for public API.
- Add a small README or usage note for the library.
- Document privacy integration for secret bookmarks.

## Integration With Stranichnik Later

The future host-app integration should look like this:

1. Load bookmarks from storage through the app's storage/application boundary.
2. Convert visible/searchable bookmarks into `BookmarkSearchDocument`.
3. Build the index with `Rebuild`.
4. On bookmark add or edit, call `AddOrUpdate`.
5. On bookmark delete, call `Remove`.
6. While secrets are locked, omit secret bookmarks.
7. After unlock, add decrypted secret bookmarks.
8. On lock, remove secret bookmarks or rebuild without them.

The search library should not reach into Stranichnik storage or view models.
The host app owns mapping, lifecycle, secret filtering, and UI presentation.

## Deferred Alternatives

### SQLite FTS5

Useful later if a persistent index becomes desirable. It is not appropriate for
the first version because the search index must not write plaintext secret terms
to disk.

### Lucene.NET

Potentially strong long-term option for advanced analyzers and ranking. It is
deferred because the first implementation should avoid additional moving parts,
persistent index files, and more complex secret-bookmark handling.

### Semantic Or Vector Search

Out of scope for the first version. It would introduce model/runtime complexity,
privacy questions, and a ranking model that is harder to test deterministically.

### Stemming And Morphology

Deferred until exact, prefix, fuzzy, and URL-aware lexical search are working.
Russian and English stemming should be added only when tests show that a
specific rule improves search quality without creating obvious false positives.

## Open Decisions For Implementation

These decisions can be finalized during coding:

- whether Form KC causes any undesirable token changes compared with Form C;
- whether prefix lookup starts as dictionary scanning or sorted-term range
  lookup;
- exact token caps after stress tests;
- exact scoring constants after ranking tests.

None of these open decisions should require changing the public API.
