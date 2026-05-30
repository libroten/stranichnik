# Search Engine Implementation Plan

This document is a step-by-step implementation plan for the standalone
`Stranichnik.Search` C# library.

It assumes the agent has already read:

1. `Notes/SEARCH_ENGINE_DESIGN.md`;
2. `Notes/SEARCH_ENGINE_ARCHITECTURE.md`;
3. `Notes/FUTURE_FEATURES_PLAN.md`;
4. `Notes/PROJECT_STATE.md`;
5. the repository-level `AGENTS.md` instructions.

The goal was to implement the search library calmly, in small testable slices,
without touching the existing Stranichnik application code. The first standalone
version has now been implemented; keep this document as the implementation
history and as a reference for future incremental search work.

## Hard Boundaries

Do not change or depend on the existing application C# code.

Allowed scope:

- `Stranichnik.Search/`;
- a new independent `Stranichnik.Search.Tests/` test project;
- search-specific documentation under `Notes/`.

Do not add references from the search library to:

- `Stranichnik.csproj`;
- `Tests/Stranichnik.Tests.csproj`;
- Avalonia;
- SQLite;
- app logging;
- settings;
- storage records;
- view models;
- encryption or sync code.

The search library may use only managed .NET APIs in version one. Do not add
native dependencies, server processes, network calls, Lucene.NET, SQLite FTS, ML,
or vector search.

The current repository instructions say not to run:

- `dotnet build`;
- `dotnet run`;
- `dotnet test`;
- `dotnet format`.

When verification is needed, ask the user to run the exact command and paste the
result. Do not run those commands yourself.

Do not run `dotnet format` in any form.

## Current Implementation State

The standalone library and test project currently exist:

```text
Stranichnik.Search/
  Stranichnik.Search.csproj
  BookmarkSearchDocument.cs
  BookmarkSearchOptions.cs
  BookmarkSearchResult.cs
  IBookmarkSearchIndex.cs
  InMemoryBookmarkSearchIndex.cs
  Internal/
  Properties/

Stranichnik.Search.Tests/
  Stranichnik.Search.Tests.csproj
```

Both projects target `net8.0`, enable nullable reference types, and include
built-in .NET analyzers.

Implemented first-version behavior:

- public API records and `IBookmarkSearchIndex`;
- standalone `InMemoryBookmarkSearchIndex`;
- exact, prefix, and fuzzy typo search;
- multi-word coverage, phrase, order, adjacency, and host bonuses;
- field-aware BM25-style scoring;
- URL-aware tokenization for host, domain parts, path/query/fragment parts, and fallback URL text;
- English/Russian practical normalization, including `ё` -> `е`;
- add/update/remove/clear/rebuild index mutations;
- failure-atomic `Rebuild`;
- `AddOrUpdate` prepares the new indexed document before replacing the old one;
- cached field-length statistics for scoring;
- sorted in-memory term set for bounded prefix expansion;
- k-gram fuzzy candidate index with reference counting;
- opt-in diagnostics;
- standalone README with usage, privacy, and threading notes;
- independent xUnit test coverage for normalization, tokenization, URL tokenization, exact search, ranking, prefix search, fuzzy search, multi-word behavior, diagnostics, validation, mutation behavior, and edge cases.

The user has confirmed successful verification after the latest review fixes:

```bash
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

## Target File Layout

The exact file layout can evolve, but prefer a clear structure like this:

```text
Stranichnik.Search/
  BookmarkSearchDocument.cs
  BookmarkSearchOptions.cs
  BookmarkSearchResult.cs
  IBookmarkSearchIndex.cs
  InMemoryBookmarkSearchIndex.cs
  Properties/
    AssemblyInfo.cs
  Internal/
    IndexedDocument.cs
    IndexedField.cs
    IndexedFieldName.cs
    IndexedTerm.cs
    InvertedIndex.cs
    Posting.cs
    SearchDiagnosticsBuilder.cs
    SearchQuery.cs
    SearchQueryParser.cs
    SearchScorer.cs
    SearchScoringParameters.cs
    SearchTextNormalizer.cs
    SearchToken.cs
    SearchTokenizer.cs
    TermMatch.cs
    TermMatchKind.cs
    TermCandidateSource.cs
    KGramTermIndex.cs
    EditDistance.cs
    UrlSearchTokenizer.cs
```

Recommended test layout:

```text
Stranichnik.Search.Tests/
  Stranichnik.Search.Tests.csproj
  SearchTextNormalizerTests.cs
  SearchTokenizerTests.cs
  UrlSearchTokenizerTests.cs
  InMemoryBookmarkSearchIndexExactTests.cs
  InMemoryBookmarkSearchIndexRankingTests.cs
  InMemoryBookmarkSearchIndexPrefixTests.cs
  InMemoryBookmarkSearchIndexFuzzyTests.cs
  InMemoryBookmarkSearchIndexUpdateTests.cs
  SearchDiagnosticsTests.cs
```

Use `InternalsVisibleTo("Stranichnik.Search.Tests")` only for focused internal
unit tests. Keep public behavior tests public-API-first wherever practical.

## Test Project Rules

If creating `Stranichnik.Search.Tests.csproj`, keep it independent:

- target `net8.0`;
- reference only `../Stranichnik.Search/Stranichnik.Search.csproj`;
- use xUnit to match the repository;
- include built-in analyzer properties required for new project files:
  `AnalysisLevel`, `AnalysisMode`, and `EnforceCodeStyleInBuild`;
- do not add a project reference to the main application.

Prefer test method names that satisfy analyzers without suppressions, for
example `SearchReturnsEmptyResultsForWhitespaceQuery`.

Do not weaken analyzer, formatting, code-quality, or health rules without first
explaining why and asking the user for confirmation.

## Verification Protocol

After each implementation phase, ask the user to run the smallest useful
verification command.

Useful commands:

```bash
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

If the test project has not been created yet, ask only for the library build.

If the user reports failures, inspect the failure output, fix the cause, and ask
the user to rerun the same command.

If a ranking test fails after a scoring change, first decide whether the product
expectation or the implementation is wrong. Do not simply rewrite tests to match
new scores. Ranking tests should usually assert relative ordering, presence, or
absence, not exact floating-point values.

## Phase 0: Preflight Review

Before writing code:

1. Read the design and architecture documents listed at the top.
2. Run read-only inspection commands such as `git status --short`, `rg`, `sed`,
   or `find`.
3. Confirm there are no accidental references from `Stranichnik.Search` to the
   main app.
4. Note unrelated dirty files and avoid touching them.

Exit criteria:

- The agent understands the standalone boundary.
- The planned next code edit is limited to the search library or its tests.

## Phase 1: Public API And Empty Index Shell

Create the public API exactly enough for callers and tests to compile:

- `BookmarkSearchDocument`;
- `BookmarkSearchOptions`;
- `BookmarkSearchResult`;
- `IBookmarkSearchIndex`;
- `InMemoryBookmarkSearchIndex`.

Implement only safe shell behavior:

- `Rebuild` validates input and indexes nothing yet.
- `AddOrUpdate` validates input and stores/removes nothing yet, or stores a
  minimal placeholder if that makes later work easier.
- `Remove` returns `false` for unknown IDs.
- `Clear` is safe on an empty index.
- `Search` returns an empty result list for any query.

Also create `Stranichnik.Search.Tests` if tests will be added now.

Tests to add:

- whitespace query returns empty results;
- empty index returns empty results;
- null document collection throws;
- null document throws;
- empty/whitespace document ID throws;
- invalid `MaxResults` throws;
- invalid `MinimumScore`, including `NaN` and infinities, throws;
- `MaxResults == 0` returns empty results.

Exit criteria:

- Public API shape is stable.
- Basic validation rules are covered.
- User confirms library build and test project test run if requested.

Suggested verification request:

```bash
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

## Phase 2: Text Normalization

Implement `SearchTextNormalizer`.

Rules:

- runtime null input becomes empty string at internal boundaries;
- Unicode normalization uses Form KC initially;
- lowercase uses invariant culture;
- Russian `ё` becomes `е`;
- repeated whitespace collapses;
- leading/trailing whitespace is trimmed;
- letters and digits are preserved.

Do not make URL-specific decisions in the normalizer. URL parsing and splitting
belong in `UrlSearchTokenizer`.

Tests to add:

- lowercase English;
- lowercase Russian;
- `ё` and `е` normalize to the same token text;
- mixed English/Russian text;
- repeated whitespace collapse;
- compatibility character behavior that justifies Form KC;
- empty and runtime null input behavior.

Exit criteria:

- Normalizer behavior is deterministic and covered by focused tests.
- No ranking or indexing logic is mixed into the normalizer.

## Phase 3: Text Tokenization And Token Filters

Implement:

- `SearchToken`;
- `SearchTokenizer`;
- small token alias/filter logic.

Rules:

- split regular text on whitespace, punctuation, and symbols;
- preserve Unicode letters and digits;
- keep one-letter tokens;
- cap indexed token length, initially 128 characters;
- cap tokens per field, initially 512;
- preserve token positions after filtering;
- normalize tokens through `SearchTextNormalizer`.

Initial aliases:

- `c#` emits `csharp` and `c`;
- `f#` emits `fsharp` and `f`;
- `.net` emits `dotnet` and `net`.

Do not implement broad English or Russian stemming in this phase.

Tests to add:

- punctuation splitting;
- mixed Russian/English tokens;
- one-letter token preservation;
- token cap behavior;
- long token truncation or dropping behavior, whichever is chosen;
- `C#`, `F#`, and `.NET` aliases;
- token position preservation for simple phrase scenarios.

Exit criteria:

- Tokenizer is independent from URL-specific parsing.
- Tests define the exact cap behavior.

## Phase 4: URL Tokenization

Implement `UrlSearchTokenizer`.

Rules:

- always tokenize original URL-like input into low-weight `url_text`;
- parse absolute URLs with `Uri.TryCreate`;
- for schemeless domain/path input such as `github.com/foo`, try a best-effort
  parse by prepending `https://` for token extraction only;
- remove leading `www.` from host tokens;
- emit full host into `url_host`;
- split host into `url_domain_parts`;
- split path, query, and fragment into `url_path_parts`;
- percent-decode segments safely;
- when IDN/punycode alternatives are available through .NET URI APIs, index both
  raw normalized and alternate forms;
- never throw for malformed URLs, malformed percent escapes, or unusual Unicode.

Tests to add:

- `https://learn.microsoft.com/dotnet/csharp/`;
- `https://github.com/AvaloniaUI/Avalonia`;
- leading `www.` removal;
- schemeless `github.com/user/repo`;
- malformed URL fallback;
- percent-encoded path;
- query string tokens;
- Cyrillic path tokens;
- best-effort IDN/punycode case, if practical without brittle platform
  assumptions.

Exit criteria:

- URL tokenizer returns field-aware tokens.
- Malformed URLs remain searchable as plain text.

## Phase 5: Indexed Document Model And Exact Search

Implement the first real in-memory index:

- `IndexedFieldName` or equivalent field identifier;
- `IndexedDocument`;
- `Posting`;
- `InvertedIndex`;
- document map by ID;
- field lengths;
- term frequencies;
- token positions.

Implement exact-token search only.

Rules:

- `Rebuild` clears and rebuilds all structures;
- duplicate IDs in `Rebuild` use last document wins;
- `AddOrUpdate` removes the old document version before adding the new one;
- `Remove` removes postings and returns whether anything was removed;
- `Clear` removes all state;
- index title and URL fields for every document;
- index tags and notes when the caller provides them;
- search scores candidate documents only.

Initial scoring can be simple field-weighted scoring before BM25 is added.

Tests to add:

- exact title token match;
- exact URL host/domain/path token match;
- exact tag and note token matches when those fields are provided;
- result IDs only, not document content;
- add document then search;
- update document removes old tokens;
- remove document removes search result;
- duplicate ID in rebuild uses last document wins;
- deterministic ordering for equal simple scores.

Exit criteria:

- Exact search works end to end through public API.
- Incremental update semantics are correct before ranking gets complicated.

## Phase 6: BM25-Style Ranking And Field Weights

Implement `SearchScoringParameters` and `SearchScorer`.

Rules:

- keep all scoring constants in one internal type;
- use BM25-style scoring per field;
- track document count;
- track document frequency per term/field;
- track average field length;
- apply field weights from the architecture document;
- sort by score descending, then normalized title, then document ID ordinal.

Tests to add:

- title match beats low-weight URL fallback match;
- host/domain match is strong;
- URL path match is useful but weaker than title/host;
- all-term match beats one-term match where expected;
- deterministic ordering across repeated searches;
- no exact floating-point score assertions unless unavoidable.

Exit criteria:

- Ranking is explainable and field-aware.
- Constants are centralized.

## Phase 7: Prefix Matching

Implement prefix term expansion behind a replaceable component.

Initial approach may scan the term dictionary, but keep this isolated so it can
later become a trie or sorted-term lookup.

Rules:

- token length 1-2 uses exact matching by default;
- token length 3+ allows prefix matches;
- prefix expansion count is capped, initially 128 per query token;
- prefix match score is lower than exact and higher than fuzzy;
- prefix expansion ordering is deterministic.

Tests to add:

- `aval` finds `avalonia`;
- `micro` finds `microsoft`;
- short token does not explode candidate count;
- exact result ranks above prefix-only result;
- prefix results are deterministic when many terms share a prefix.

Exit criteria:

- Partial remembered words work without full-corpus scoring.

## Phase 8: Fuzzy Matching

Implement:

- `KGramTermIndex`;
- bounded `EditDistance`;
- fuzzy term candidate retrieval;
- fuzzy scoring penalties.

Rules:

- use trigrams for normal tokens;
- do not fuzzy-match query tokens shorter than 3 characters;
- length 3-5 allows edit distance 1;
- length 6+ allows edit distance 2;
- use gram overlap to prune candidates before edit distance;
- cap fuzzy candidates after gram filtering, initially 256 per query token;
- score fuzzy distance 1 above fuzzy distance 2;
- exact and prefix matches should rank above fuzzy matches when other factors
  are comparable.

Tests to add:

- `avlaonia` finds `avalonia`;
- one typo in medium token;
- two typos in longer token;
- short token does not fuzzy-match broad unrelated terms;
- exact match ranks above fuzzy typo match;
- fuzzy candidate caps preserve deterministic behavior.

Exit criteria:

- Typo-tolerant search works without comparing every query token to every term.

## Phase 9: Multi-Word Quality, Phrase, Proximity, And URL Boosts

Improve quality for remembered phrases and rough word order.

Rules:

- track which query tokens matched each document;
- apply query coverage multiplier;
- add exact normalized phrase bonus for title;
- add exact normalized phrase bonus for URL/host;
- add ordered-token and adjacent-token bonuses within a field;
- add host/domain-specific boosts;
- keep bonuses small enough that weak matches do not beat strong exact matches.

Tests to add:

- `avalonia docs` ranks the expected documentation bookmark first;
- `learn microsoft` strongly matches `learn.microsoft.com`;
- `dotnet csharp` finds `.NET`/`C#` documentation;
- all query terms matched ranks above one-term-only result;
- phrase in title ranks above scattered terms where appropriate;
- changed word order still returns useful results.

Exit criteria:

- Multi-word query behavior feels better than independent single-token search.

## Phase 10: Diagnostics

Implement diagnostics only when `IncludeDiagnostics = true`.

Rules:

- diagnostics are null by default;
- no diagnostics are logged by the library;
- diagnostics may include sensitive query/index terms, so keep them concise;
- include enough detail for tests and future tuning.

Possible diagnostics:

- exact/prefix/fuzzy match fragments;
- field names;
- fuzzy distance;
- query coverage;
- phrase/proximity/domain boost markers.

Tests to add:

- diagnostics are null by default;
- diagnostics are present when enabled;
- diagnostics mention exact/prefix/fuzzy matches in broad terms;
- enabling diagnostics does not change ordering or scores.

Exit criteria:

- Diagnostics help tuning without changing normal privacy behavior.

## Phase 11: Edge Cases, Caps, And Stress Tests

Harden the implementation.

Tests to add:

- very long title;
- very long URL;
- very long query;
- empty title and URL;
- unusual Unicode;
- many repeated terms;
- large prefix candidate set;
- large fuzzy candidate set;
- search over cleared index;
- remove unknown ID;
- repeated add/update/remove for the same ID.

If adding larger stress tests, keep them deterministic and reasonably fast.
Avoid introducing benchmark infrastructure unless the user agrees.

Exit criteria:

- Long or strange user input does not produce pathological behavior.

## Phase 12: XML Docs And Usage Notes

Add XML documentation to public types and members.

Add a small usage note, either in a library README or a Notes document:

- how to create documents;
- how to rebuild the index;
- how to update on add/edit/delete;
- how to handle locked/unlocked secret bookmarks;
- that the index is in-memory and not thread-safe;
- that diagnostics may contain sensitive terms.

Exit criteria:

- A future integration agent can use the library without reading internals.

## Phase 13: Final Integration Readiness Review

Before saying the library is ready for application integration:

1. Re-read `Notes/SEARCH_ENGINE_ARCHITECTURE.md`.
2. Confirm all hard requirements from `Notes/SEARCH_ENGINE_DESIGN.md` are met or
   explicitly deferred.
3. Confirm no app/UI/storage dependencies were introduced.
4. Confirm no index persistence or logging was introduced.
5. Ask the user to run:

```bash
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

6. Update `Notes/PROJECT_STATE.md`.
7. If architectural decisions changed, update
   `Notes/SEARCH_ENGINE_ARCHITECTURE.md` and, if relevant,
   `Notes/FUTURE_FEATURES_PLAN.md`.

Exit criteria:

- The library builds independently.
- Tests pass according to the user-provided output.
- The public API is small and stable.
- Search quality meets the first-version requirements.
- Privacy boundaries remain intact.
