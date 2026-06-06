# Search Engine Integration Guide

This document is a handoff guide for an agent that will integrate the standalone
`Stranichnik.Search` library into the Stranichnik desktop application.

The search library itself already exists in `Stranichnik.Search/`.

Current status: the first application integration has been implemented. This
document remains useful as historical handoff context and as guidance for future
search refinements.

## Read First

Before editing code, read:

1. `AGENTS.md`;
2. `Notes/ARCHITECTURE.md`;
3. `Notes/PROJECT_STATE.md`;
4. `Notes/FUTURE_FEATURES_PLAN.md`;
5. `Notes/STORAGE_ARCHITECTURE.md`;
6. `Notes/SEARCH_ENGINE_DESIGN.md`;
7. `Notes/SEARCH_ENGINE_ARCHITECTURE.md`;
8. `Notes/SEARCH_ENGINE_IMPLEMENTATION_PLAN.md`;
9. `Stranichnik.Search/README.md`.

The repository instructions say not to run build, test, run, or format commands.
When verification is needed, ask the user to run the exact command and paste the
output.

Useful commands to ask the user for:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

Do not run `dotnet format`.

## Current Search Library

Public API namespace:

```csharp
using Stranichnik.Search;
```

Main public types:

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
    public double MinimumScore { get; init; }
    public bool IncludeDiagnostics { get; init; }
}

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

Concrete implementation:

```csharp
IBookmarkSearchIndex index = new InMemoryBookmarkSearchIndex();
```

Important behavior:

- the index is in-memory only;
- it does not log and does not write index files;
- results return IDs and metadata, not bookmark content;
- `Rebuild` is failure-atomic;
- `AddOrUpdate` prepares the replacement document before replacing the old one;
- the implementation is not thread-safe.

## Integration Boundaries

Keep the library boundary clean:

- the application may reference `Stranichnik.Search`;
- `Stranichnik.Search` must not reference the application project;
- do not add Avalonia, SQLite, logging, settings, encryption, or sync references
  to the search library;
- do not modify the search library unless integration uncovers a real library
  bug; if that happens, add/adjust standalone search tests first.

Search integration should sit behind an application-level service boundary, not
inside Avalonia event handlers.

Suggested application-side namespace:

```csharp
namespace Stranichnik.Searching;
```

Using `Searching` avoids name conflicts with the `Stranichnik.Search` library
namespace.

## Current Integration Shape

The main app project references the search library:

```xml
<ProjectReference Include="Stranichnik.Search/Stranichnik.Search.csproj" />
```

Application-side files:

```text
Searching/
  BookmarkSearchDocumentMapper.cs
  BookmarkSearchService.cs
  BookmarkSearchResultItem.cs
```

Optional later files:

```text
Searching/
  IBookmarkSearchService.cs
```

An interface may be useful if tests need to inject a fake search service into view
models. If the first integration stays small, a concrete service is acceptable.

## Mapping Storage Records To Search Documents

Current storage records:

```csharp
BookmarkTreeSnapshot snapshot;
IReadOnlyList<BookmarkItemRecord> snapshot.Items;
```

Only index visible bookmarks:

- `Kind == BookmarkItemKind.Bookmark`;
- `Metadata.DeletedAtUtc is null`;
- `Title` and `Url` may be normalized to empty strings if null.

Do not index folders in the first version.

Do not index raw locked secret records. Secret bookmarks may enter the in-memory
search index only after the secret-bookmark projection has decrypted them for the
current visible UI state. Hidden secret bookmarks must be absent from search.

Suggested mapper shape:

```csharp
internal static class BookmarkSearchDocumentMapper
{
    public static IEnumerable<BookmarkSearchDocument> CreateDocuments(
        BookmarkTreeSnapshot snapshot)
    {
        return snapshot.Items
            .Where(CanIndex)
            .Select(CreateDocument);
    }

    public static bool CanIndex(BookmarkItemRecord record)
    {
        return record.Kind == BookmarkItemKind.Bookmark
            && record.Metadata.DeletedAtUtc is null
            && !record.IsSecret;
    }

    public static BookmarkSearchDocument CreateDocument(BookmarkItemRecord record)
    {
        return new BookmarkSearchDocument(
            record.Id,
            record.Title ?? string.Empty,
            record.Url ?? string.Empty);
    }
}
```

Keep tags and notes null until the application has storage/UI support for them.

## Application Search Service

The service should own `IBookmarkSearchIndex`, not the view or event handlers.

Suggested responsibilities:

- `Rebuild(BookmarkTreeSnapshot snapshot)`;
- `AddOrUpdate(BookmarkItemRecord record)`;
- `Remove(string id)`;
- `Search(string query, BookmarkSearchOptions? options = null)`;
- optionally map result IDs back to currently visible `BookmarkViewModel`
  instances at the view-model layer.

Minimal service sketch:

```csharp
public sealed class BookmarkSearchService
{
    private readonly IBookmarkSearchIndex _index;

    public BookmarkSearchService(IBookmarkSearchIndex index)
    {
        _index = index;
    }

    public void Rebuild(BookmarkTreeSnapshot snapshot)
    {
        _index.Rebuild(BookmarkSearchDocumentMapper.CreateDocuments(snapshot));
    }

    public void AddOrUpdate(BookmarkItemRecord record)
    {
        if (BookmarkSearchDocumentMapper.CanIndex(record))
            _index.AddOrUpdate(BookmarkSearchDocumentMapper.CreateDocument(record));
        else
            _index.Remove(record.Id);
    }

    public bool Remove(string id)
    {
        return _index.Remove(id);
    }

    public IReadOnlyList<BookmarkSearchResult> Search(
        string query,
        BookmarkSearchOptions? options = null)
    {
        return _index.Search(query, options);
    }
}
```

Do not enable diagnostics in normal UI flows. Diagnostics may include query terms
and matched indexed tokens.

## Startup Wiring

Current app startup creates the tree store in `App.axaml.cs` and injects it into
`MainWindowViewModel`.

Recommended first step:

1. Create the `IBookmarkTreeStore`.
2. Create `BookmarkSearchService` with `new InMemoryBookmarkSearchIndex()`.
3. Inject both into `MainWindowViewModel`.

Inside `MainWindowViewModel`, avoid loading storage twice. Load the snapshot once,
rebuild search from that snapshot, and create tree view models from the same
snapshot:

```csharp
BookmarkTreeSnapshot snapshot = _treeStore.Load();
_searchService.Rebuild(snapshot);

var items = BookmarkTreeViewModelMapper.CreateViewModels(
    snapshot,
    expandedFolderIds);
```

This keeps the initial UI tree and search index based on the same storage view.

## Updating The Index During CRUD

Update search only after the store operation succeeds.

Add bookmark:

- call `_treeStore.AddBookmarkToFolderStart(...)`;
- create the `BookmarkViewModel`;
- call `_searchService.AddOrUpdate(record)`.

Edit bookmark:

- call `_treeStore.EditBookmark(...)`;
- update the `BookmarkViewModel`;
- call `_searchService.AddOrUpdate(record)`.

Delete bookmark:

- call `_treeStore.DeleteItem(item.Id)`;
- remove the view model from the tree;
- call `_searchService.Remove(item.Id)`.

Delete folder:

- the storage layer deletes the folder and all descendants;
- remove the folder view model from the tree;
- either remove all descendant bookmark IDs from the index before clearing their
  parent links, or simply call `_searchService.Rebuild(_treeStore.Load())` after
  the delete succeeds.

For the first integration, rebuilding search after a folder delete is simpler and
less error-prone.

Move item:

- moving does not change bookmark title or URL;
- no search update is required unless later ranking starts using folder path or
  parent context.

Edit folder:

- folder search is not part of the first version;
- no search update is required.

## View Model Search State

Keep the first UI integration modest.

Suggested `MainWindowViewModel` additions:

- `SearchQuery`;
- `SearchResults`;
- `IsSearchActive`;
- a method/command that updates results when `SearchQuery` changes.

Do not rebuild or mutate the bookmark tree just to show search results in the
first version. Filtering the existing tree can interfere with expansion state,
drag-and-drop, parent links, and delete/move behavior.

Safer first UI:

- keep the existing tree unchanged;
- show a separate search result list/panel;
- each result maps back to a bookmark by ID;
- result item displays title and URL from the current view model or from a small
  application-side lookup.

Later, result click can reveal/select the bookmark in the tree.

## Result Mapping

The search library returns IDs only. The application must map IDs back to visible
bookmarks.

Possible first implementation:

- maintain a `Dictionary<string, BookmarkViewModel>` in `MainWindowViewModel`;
- rebuild it after initial tree creation;
- update it on add/edit/delete;
- use it to create display result items from `BookmarkSearchResult.Id`.

Do not expose secret bookmark data through search result objects. While secrets
are locked, secret bookmarks must not be indexed and therefore must not appear in
results.

## UI And Localization

If UI is added:

- put user-visible strings in `Resources/Strings.resx` and
  `Resources/Strings.ru.resx`;
- expose strings through `Localization/UiStrings.cs`;
- avoid hardcoded UI text in XAML/code-behind except during a temporary active
  refactor;
- follow existing visual style in `Views/MainWindow.axaml`;
- avoid relying on default Avalonia visual templates for important new controls;
- keep the first search UI compact and work-focused, not a landing-page style
  surface.

Likely UI strings:

- search placeholder;
- empty search result text;
- no matches text;
- result count text if shown;
- clear search tooltip.

## Tests To Add

Prefer service/view-model tests before UI tests.

Suggested tests:

- mapper indexes only visible non-secret bookmarks;
- mapper ignores folders;
- mapper ignores deleted records;
- mapper ignores secret records;
- service `Rebuild` indexes current snapshot;
- service `AddOrUpdate` indexes new bookmark records;
- service `AddOrUpdate` removes records that are no longer indexable;
- service `Remove` removes deleted bookmark IDs;
- `MainWindowViewModel` rebuilds search from the same snapshot used for the tree;
- add bookmark updates search results;
- edit bookmark updates search results;
- delete bookmark removes it from search results;
- folder delete does not leave descendant bookmarks in search results.

If a test fails after a UI or ranking change, first decide whether the expected
product behavior changed or whether the implementation is wrong. Do not simply
rewrite ranking expectations to match accidental behavior.

## Privacy And Future Secret Bookmarks

Current storage already has `IsSecret` and `EncryptedPayload` fields.

Until secret unlock/decryption exists:

- never index `IsSecret == true` records;
- never log search query text, bookmark titles, URLs, or diagnostics;
- keep all search index state in memory;
- on future lock, remove secret IDs or rebuild without secret documents;
- on app exit, discard the index with the process.

Do not implement searchable encryption. It is out of scope and easy to get
wrong.

## Implementation Phases

### Phase 1: Reference And Service

1. Add the project reference from the main app to `Stranichnik.Search`.
2. Add `Searching/BookmarkSearchDocumentMapper.cs`.
3. Add `Searching/BookmarkSearchService.cs`.
4. Add focused tests for mapper and service.
5. Ask the user to run:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
```

Status: implemented.

### Phase 2: View Model Wiring

1. Inject `BookmarkSearchService` into `MainWindowViewModel`.
2. Rebuild search from the initial storage snapshot.
3. Update search on add/edit/delete bookmark.
4. Rebuild or remove descendants on folder delete.
5. Add view-model tests for search synchronization.
6. Ask the user to run build and tests again.

Status: implemented.

### Phase 3: Minimal UI

1. Add localized UI strings.
2. Add a search input and separate result list/panel.
3. Keep the bookmark tree unchanged while showing results.
4. Add clear-search behavior.
5. Verify manually with normal app launch and sample-data launch.

Status: implemented. Current UI shows a search bar above the work area. While
search is active, search results replace the bookmark tree in the same area.
Search result rows include an open action.

### Phase 4: Polish

1. Add keyboard focus behavior.
2. Add result click behavior to reveal/select a bookmark if needed.
3. Tune spacing, empty states, and long URL/title layout.
4. Consider debouncing only if search feels too eager on large datasets.

Status: optional future work.

## Things To Avoid

- Do not couple `Stranichnik.Search` to the app.
- Do not make search depend on SQLite directly.
- Do not scrape Avalonia controls to build the index.
- Do not index folders in the first integration.
- Do not index secret bookmarks while locked.
- Do not persist search index data.
- Do not log queries, indexed tokens, titles, URLs, or diagnostics.
- Do not mutate the tree structure as a search filtering mechanism in the first
  UI pass.
- Do not run build, test, app, or format commands yourself; ask the user.
