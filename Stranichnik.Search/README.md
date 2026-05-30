# Stranichnik.Search

Standalone in-memory bookmark search library for Stranichnik-like bookmark
managers.

The library is intentionally independent from the Stranichnik Avalonia app,
SQLite storage, logging, settings, encryption, and sync code.

## Basic Usage

```csharp
using Stranichnik.Search;

IBookmarkSearchIndex index = new InMemoryBookmarkSearchIndex();

index.Rebuild(
[
    new BookmarkSearchDocument(
        Id: "bookmark-1",
        Title: "Avalonia Documentation",
        Url: "https://docs.avaloniaui.net/",
        Tags: ["ui", "dotnet"]),
    new BookmarkSearchDocument(
        Id: "bookmark-2",
        Title: ".NET C# Guide",
        Url: "https://learn.microsoft.com/dotnet/csharp/")
]);

IReadOnlyList<BookmarkSearchResult> results = index.Search("aval docs");
```

Search results return document IDs and ranking metadata. They do not return full
bookmark content. The caller should map result IDs back to its own bookmark
records.

## Updating The Index

Call `AddOrUpdate` after creating or editing a bookmark:

```csharp
index.AddOrUpdate(new BookmarkSearchDocument(
    Id: "bookmark-3",
    Title: "GitHub",
    Url: "https://github.com/"));
```

Call `Remove` after deleting a bookmark:

```csharp
bool removed = index.Remove("bookmark-3");
```

Call `Clear` or `Rebuild` when the host application wants to discard all indexed
state.

`Rebuild` prepares a replacement index before swapping it in, so the previous
index remains usable if input validation fails while rebuilding. `AddOrUpdate`
also prepares the new indexed document before replacing the old document with
the same ID.

## Search Behavior

The first implementation supports:

- exact token matches;
- prefix matches;
- typo-tolerant fuzzy matches;
- multi-word query coverage;
- simple phrase, order, and adjacency bonuses;
- URL host, domain part, and path matching;
- English and Russian text normalization, including `ё` -> `е`;
- deterministic result ordering.

Prefix lookup uses a sorted in-memory term set. Fuzzy lookup uses a k-gram
candidate index with edit-distance verification. All index state remains in
memory.

The URL field is a string by design. The library accepts malformed, schemeless,
or URL-like text and tokenizes it on a best-effort basis.

## Diagnostics

Diagnostics are disabled by default:

```csharp
IReadOnlyList<BookmarkSearchResult> normalResults = index.Search("avalonia");
```

They can be enabled for development and ranking tests:

```csharp
IReadOnlyList<BookmarkSearchResult> diagnosticResults = index.Search(
    "avlaonia docs",
    new BookmarkSearchOptions
    {
        IncludeDiagnostics = true
    });
```

Diagnostics may contain query terms, matched indexed tokens, field names, host
names, and fuzzy-match mappings. Do not log or show diagnostics in normal user
flows unless the caller explicitly accepts that exposure.

## Secret Bookmarks

The search library does not know whether a bookmark is secret. It only indexes
the plaintext documents passed by the caller.

The host application must enforce privacy:

- while locked, do not pass secret bookmarks to `Rebuild`;
- after unlock, decrypt secret bookmarks outside the library and call
  `AddOrUpdate`;
- on lock, call `Remove` for secret bookmark IDs or rebuild without secret
  bookmarks;
- on app exit, discard the in-memory index.

The library does not write index data to disk and does not perform logging.

## Threading

`InMemoryBookmarkSearchIndex` is not thread-safe. Do not call `Search`
concurrently with `Rebuild`, `AddOrUpdate`, `Remove`, or `Clear` on the same
instance.

If a host application needs background search while bookmarks are mutated, add a
synchronization wrapper or use immutable index snapshots at the host level.
