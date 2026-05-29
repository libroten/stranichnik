# Stranichnik Storage Architecture Draft

This note records the current working design for the future storage/repository layer.

Important: this is not a fixed contract. It is a practical direction for the next persistence steps and may change if implementation reveals simpler or safer choices.

## Goal

The project needs a storage boundary between Avalonia view models and the future SQLite database.

The storage design should support:

- Current in-memory bookmark/folder CRUD behavior.
- Future SQLite persistence.
- Future selective encryption.
- Future in-memory search indexing.
- Future WebDAV item-level sync.
- Tombstone-based deletes.

The storage design should avoid:

- Letting Avalonia view models become the persistent data model.
- Spreading SQLite calls through XAML code-behind or view models.
- Updating UI collections directly in ways that cannot later be mirrored to disk, search, encryption, and sync metadata.

## Current Shape

Current simplified flow:

```text
Avalonia UI
  -> MainWindowViewModel
    -> IBookmarkTreeStore
      -> InMemoryBookmarkTreeStore
```

This is the current in-memory storage boundary. SQLite should replace or supplement the store implementation behind the same interface rather than being called directly from UI code.

## Target Shape

Target simplified flow:

```text
Avalonia UI
  -> ViewModels
    -> ViewModels / future application service
      -> IBookmarkTreeStore
        -> SQLite
```

Responsibilities:

- UI and view models: screen state, selection/hover/drag visuals, dialog wiring.
- Application service: bookmark operations and business rules.
- Store/repository: persistence, timestamps, revision/sync metadata, SQLite mapping.
- Search/encryption/sync services: separate concerns that can observe or be called from the application/storage boundary.

## Domain And Storage Records

Do not make Avalonia view models the source of truth for persisted data.

Use plain domain/storage records that map naturally to the future SQLite schema.

Current draft:

```csharp
public enum BookmarkItemKind
{
    Folder,
    Bookmark
}
```

```csharp
public sealed record BookmarkItemRecord(
    string Id,
    string? ParentId,
    BookmarkItemKind Kind,
    long SortOrder,
    string? Title,
    string? Url,
    bool IsSecret,
    EncryptedBookmarkPayloadRecord? EncryptedPayload,
    BookmarkItemMetadata Metadata);
```

```csharp
public sealed record EncryptedBookmarkPayloadRecord(
    byte[] Payload,
    byte[] Nonce,
    long CryptoProfileId);
```

```csharp
public sealed record BookmarkItemMetadata(
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    int Revision,
    string SyncState,
    string? RemoteEtag,
    DateTimeOffset? LastSyncedAtUtc,
    string ModifiedDeviceId);
```

These names and exact field types can still change, but the important idea is stable:

- Data records are UI-independent.
- Records contain persistence/sync/encryption metadata.
- View models are created from records, not stored directly in SQLite.

## Tree Snapshot

Loading should return a flat snapshot of records:

```csharp
public sealed record BookmarkTreeSnapshot(
    IReadOnlyList<BookmarkItemRecord> Items);
```

Reason:

- SQLite rows are naturally flat.
- Parent/child structure is represented by `ParentId`.
- The view-model layer can build the visible tree from the flat snapshot.
- Search/sync services can also consume flat records.

The synthetic root folder is not part of the snapshot. Top-level items have `ParentId = null`.

## Store Interface

The first repository/store interface should match the operations the app already supports.

Current draft:

```csharp
public interface IBookmarkTreeStore
{
    BookmarkTreeSnapshot Load();

    BookmarkItemRecord AddBookmarkToFolderStart(
        string? parentId,
        string title,
        string url);

    BookmarkItemRecord AddFolderToFolderStart(
        string? parentId,
        string title);

    BookmarkItemRecord EditBookmark(
        string bookmarkId,
        string title,
        string url);

    BookmarkItemRecord EditFolder(
        string folderId,
        string title);

    void DeleteItem(string itemId);

    bool CanMoveToFolderStart(
        string itemId,
        string? targetParentId);

    BookmarkItemRecord MoveToFolderStart(
        string itemId,
        string? targetParentId);
}
```

Notes:

- `parentId = null` means top-level item under the synthetic root.
- The store should hide tombstoned items from normal `Load` results.
- Delete is tombstone-based even in the in-memory store.
- Store methods should be transactional where needed.
- The interface is intentionally synchronous for the current local in-memory/SQLite direction. It may become async later if real workloads require it.

## View Model Operation Boundary

The old `BookmarkTreeService` has been removed.

`MainWindowViewModel` currently acts as the operation boundary:

- It calls `IBookmarkTreeStore` first.
- It updates the visible tree after successful store operations.
- It returns operation result records from `ViewModels/BookmarkTreeOperationResults.cs`.

Responsibilities:

- Normalize user input.
- Enforce rules that are not UI-only.
- Call `IBookmarkTreeStore`.
- Return operation results useful for view models.
- Keep add/edit/delete/move behavior centralized.

A future `BookmarkTreeApplicationService` may still be introduced if `MainWindowViewModel` becomes too large or if search/encryption/sync hooks need a clearer orchestration layer.

Important rules to keep centralized:

- Do not move a folder into itself.
- Do not move a folder into its own descendant.
- Do not edit/delete/drag the synthetic root.
- New and moved items go to the start of the target folder.

## In-Memory Store First

Completed implementation order:

1. Add domain/storage records.
2. Add `IBookmarkTreeStore`.
3. Add `InMemoryBookmarkTreeStore`.
4. Adapt current view models/application service to load from the store.
5. Keep behavior visually identical.
6. Add tests around store operations.
7. Move add/edit/delete/move operations to the store-backed path.

Next implementation step:

8. Add `SqliteBookmarkTreeStore` after the boundary is stable.

Reason:

- This keeps persistence work incremental.
- It reduces risk of breaking the current UI while introducing SQLite.
- It allows the repository contract to be tested before database details enter the codebase.

## Future SQLite Store

`SqliteBookmarkTreeStore` should be responsible for:

- Opening the app data database.
- Applying explicit migrations.
- Mapping SQLite rows to `BookmarkItemRecord`.
- Generating item IDs.
- Allocating `sort_order`.
- Maintaining timestamps.
- Incrementing revisions.
- Marking changed rows as `sync_state = dirty`.
- Tombstoning deleted items.
- Running multi-row operations in transactions.

It should not be responsible for:

- Avalonia control state.
- Row hover/drag visuals.
- Dialog behavior.
- Search ranking.
- WebDAV transport.

## Search Interaction

Search should not scrape UI controls.

Search should consume bookmark records or domain projections from the application/storage boundary.

First implementation direction:

- Build an in-memory search index from loaded non-secret bookmark records.
- After secret unlock, decrypt secret bookmarks and add them to the in-memory index.
- Update the index after add/edit/delete/move through centralized operation results.

No persistent plaintext search index should be written for secret bookmarks.

## Encryption Interaction

Encryption should not be implemented inside view models.

Future direction:

- Normal bookmarks use plaintext `Title` and `Url`.
- Secret bookmarks use `IsSecret = true` and `EncryptedPayload`.
- Decrypted secret fields live in memory only after unlock.
- The storage layer persists encrypted bytes and metadata.
- A separate encryption service should own password-derived keys and encryption/decryption operations.

The exact boundary between encryption service and store can be refined later. The important constraint is that UI code should not implement crypto.

## Sync Interaction

Sync should not operate on view models.

Future direction:

- Sync reads and writes item-level records through storage/repository APIs.
- SQLite remains local working storage.
- WebDAV stores item-level remote objects, not the SQLite database file.
- Tombstones are used to propagate deletes.
- Conflicts should preserve data conservatively.

The repository should maintain enough metadata for sync, but WebDAV transport and conflict UI should be separate concerns.

## Open Questions

These can wait until implementation:

- Exact namespace names.
- Whether `BookmarkItemRecord` should be class or record.
- Whether metadata should stay nested or be flattened.
- Whether store methods should return full records or operation result objects.
- How much validation belongs in application service versus store.
- Whether `Load` should include locked secret rows as metadata-only records or hide them until unlock.

## Recommended Next Step

Add SQLite persistence behind `IBookmarkTreeStore`.

Do this incrementally:

1. Add SQLite package/reference and database path.
2. Add migration runner.
3. Create the first schema from `Notes/DATA_SCHEMA.md`.
4. Implement `SqliteBookmarkTreeStore`.
5. Keep `InMemoryBookmarkTreeStore` tests as contract guidance.
