# Stranichnik SQLite Data Schema Draft

This note records the current working design for the future SQLite storage layer.

Important: this is not a fixed contract. It is a practical starting point that should guide the first persistence implementation, but it may change during development if encryption, search, synchronization, or UI requirements reveal a better shape.

## Goals

The schema should support:

- The current tree UI with folders and bookmarks.
- A synthetic root folder in the UI.
- New and moved items being placed at the start of a folder.
- Future selective encryption for secret bookmarks.
- Future in-memory search indexing.
- Future WebDAV item-level sync.
- Conservative delete behavior through tombstones.

The schema should avoid:

- Storing user-visible UI state as the source of truth.
- Syncing the whole SQLite database file through WebDAV.
- Persisting plaintext search terms for secret bookmarks.
- Treating the synthetic root folder as normal user data.

## Main Design Choice

Use one table, `items`, for both folders and bookmarks.

Reasons:

- The current application model already treats folders and bookmarks as tree items.
- Add, edit, delete, and move operations can be kept generic.
- Parent/child relationships need one consistent representation.
- Future sync can work with one stable item identity model.
- Future tombstones and conflict handling are easier when all tree entities share metadata.

Alternative considered:

- Separate `folders` and `bookmarks` tables.

That alternative gives stricter table-level typing, but it complicates parent references, ordering, delete cascades, sync objects, and generic tree operations. The current recommendation is a single `items` table with type constraints.

## Synthetic Root

The visible root folder, currently shown as `Все закладки`, should not be stored as a normal row.

Database rule:

- Top-level items have `parent_id = NULL`.

UI rule:

- The application creates a synthetic root view model around top-level database items.
- The root title is UI text and should be localized.
- The root cannot be edited, deleted, dragged, or synced as user data.

## Schema Versioning And Metadata

Use SQLite `PRAGMA user_version` for schema migrations.

Use `app_meta` for stable app/database metadata:

```sql
CREATE TABLE app_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

Expected keys:

- `database_id`: stable identifier for this local database.
- `device_id`: stable identifier for this application installation/device.

The exact migration system can be designed later, but all schema changes should be explicit and versioned.

## Crypto Metadata

Secret bookmark encryption will need KDF/encryption metadata.

Initial table draft:

```sql
CREATE TABLE crypto_profiles (
    id INTEGER PRIMARY KEY,
    kdf_name TEXT NOT NULL,
    kdf_iterations INTEGER NOT NULL,
    salt BLOB NOT NULL,
    encryption_algorithm TEXT NOT NULL,
    password_check_payload BLOB NULL,
    password_check_nonce BLOB NULL,
    created_at_utc TEXT NOT NULL
);
```

Current direction:

- Use application-level payload encryption.
- Do not encrypt the whole database file by default.
- Secret bookmarks hide sensitive fields inside `encrypted_payload`.
- Maximum cryptographic sophistication is not the goal for the first version.
- Do not invent custom cryptography.

Likely first implementation:

- PBKDF2-derived key from the master password.
- AES-GCM encrypted bookmark payloads.

Possible later implementation:

- Random data encryption key.
- Password-derived key wrapping the data encryption key.

## Items Table

Initial table draft:

```sql
CREATE TABLE items (
    id TEXT PRIMARY KEY,

    parent_id TEXT NULL REFERENCES items(id) ON DELETE RESTRICT,

    item_type TEXT NOT NULL CHECK (item_type IN ('folder', 'bookmark')),
    sort_order INTEGER NOT NULL,

    title TEXT NULL,
    url TEXT NULL,

    is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1)),
    encrypted_payload BLOB NULL,
    encryption_nonce BLOB NULL,
    crypto_profile_id INTEGER NULL REFERENCES crypto_profiles(id),

    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    deleted_at_utc TEXT NULL,

    revision INTEGER NOT NULL DEFAULT 1,
    content_hash TEXT NULL,

    sync_state TEXT NOT NULL DEFAULT 'dirty'
        CHECK (sync_state IN ('clean', 'dirty', 'conflict')),

    remote_etag TEXT NULL,
    last_synced_at_utc TEXT NULL,
    modified_device_id TEXT NOT NULL,

    CHECK (parent_id IS NULL OR parent_id <> id),

    CHECK (
        (item_type = 'folder'
            AND is_secret = 0
            AND title IS NOT NULL
            AND url IS NULL
            AND encrypted_payload IS NULL
            AND encryption_nonce IS NULL
            AND crypto_profile_id IS NULL)

        OR

        (item_type = 'bookmark'
            AND is_secret = 0
            AND title IS NOT NULL
            AND url IS NOT NULL
            AND encrypted_payload IS NULL
            AND encryption_nonce IS NULL
            AND crypto_profile_id IS NULL)

        OR

        (item_type = 'bookmark'
            AND is_secret = 1
            AND title IS NULL
            AND url IS NULL
            AND encrypted_payload IS NOT NULL
            AND encryption_nonce IS NOT NULL
            AND crypto_profile_id IS NOT NULL)
    )
);
```

Recommended indexes:

```sql
CREATE INDEX idx_items_parent_order
    ON items(parent_id, deleted_at_utc, sort_order DESC, id);

CREATE INDEX idx_items_sync_state
    ON items(sync_state);

CREATE INDEX idx_items_updated_at
    ON items(updated_at_utc);
```

## Field Notes

`id`:

- Stable item identifier.
- Should be generated by the application, not by SQLite rowid.
- A GUID string is enough for the first implementation.
- A ULID remains possible later if sortable IDs become useful.

`parent_id`:

- Points to a folder item.
- `NULL` means top-level item under the synthetic root.
- SQLite can prevent direct self-parenting, but recursive cycles must still be prevented in application logic.

`item_type`:

- `folder` or `bookmark`.

`sort_order`:

- Defines sibling order.
- The UI currently supports only "insert/move to folder start".
- Sort with `sort_order DESC`.
- Initial simple strategy: new item gets `max(sort_order) + 1000` in the target folder.
- If gaps become small or values grow too much, siblings can be rebalanced in one transaction.
- If sync later creates equal values, use `id` as deterministic tie-breaker.

`title` and `url`:

- Plaintext for normal bookmarks and folders.
- `url` is only valid for normal bookmarks.
- Secret bookmarks keep these columns `NULL`.

`encrypted_payload`:

- Stores sensitive secret bookmark data.
- Expected payload content can be JSON before encryption:

```json
{
  "title": "Secret title",
  "url": "https://example.com/",
  "notes": "",
  "tags": []
}
```

`deleted_at_utc`:

- `NULL` means visible/not deleted.
- Non-`NULL` means tombstoned.
- UI queries should hide tombstoned items.
- Sync needs tombstones so deletes can propagate.

`revision`:

- Local monotonic version for the item.
- Increment on meaningful user changes.
- Useful for sync and conflict diagnostics.

`content_hash`:

- Optional hash of the normalized sync payload.
- Useful later for detecting no-op changes and conflicts.

`sync_state`:

- `dirty`: local change not known to be uploaded.
- `clean`: local state known to match remote state.
- `conflict`: sync detected divergent versions that need user-safe preservation.

`remote_etag`:

- Stores WebDAV ETag for item-level remote object sync.
- Exact semantics depend on the future remote object format.

## Secret Bookmark Behavior

Normal bookmark row:

- `is_secret = 0`
- `title` and `url` are plaintext.
- `encrypted_payload = NULL`

Secret bookmark row:

- `is_secret = 1`
- `title = NULL`
- `url = NULL`
- `encrypted_payload` contains title, url, and future searchable text.

While locked:

- Secret bookmarks are not shown in the UI.
- Secret bookmarks are not included in search results.

After unlock:

- Secret payloads can be decrypted in memory.
- Decrypted secret bookmarks can be displayed.
- Secret bookmark text can be added to the in-memory search index.

When locked again or when the app exits:

- Decrypted secret data and secret search terms should be discarded from memory as much as practical.

Known privacy tradeoff:

- The database may reveal that a secret row exists, where it is in the tree, and when it was modified.
- The database should not reveal the secret title or URL without the password.

## Search Interaction

Do not create a persistent plaintext search index for secret bookmarks.

First implementation direction:

- Load normal bookmarks from SQLite.
- Build an in-memory search index.
- After secret unlock, decrypt secret bookmarks and add them to the same in-memory index.
- On secret lock or app shutdown, discard secret index data.

This schema intentionally does not include a search index table.

If a persistent search engine is added later:

- It must either exclude secret bookmarks or store only encrypted/non-sensitive data for them.
- SQLite FTS5 can be considered for non-secret bookmarks.
- Lucene.NET can be considered if search becomes a defining feature.

## Sync Interaction

The database should remain local working storage.

WebDAV sync should not upload/download the SQLite file directly.

Preferred future direction:

- Each logical item is represented as an item-level remote sync object.
- Local SQLite rows track enough metadata to know what changed.
- Deletes are tombstones first, not immediate physical removals.
- Conflicts should preserve data conservatively instead of overwriting silently.

Fields intended for sync:

- `id`
- `updated_at_utc`
- `deleted_at_utc`
- `revision`
- `content_hash`
- `sync_state`
- `remote_etag`
- `last_synced_at_utc`
- `modified_device_id`

Conflict policy is not finalized yet. A safe first policy can create conflict copies rather than trying to merge aggressively.

## Operation Mapping

Add bookmark:

- Insert `item_type = 'bookmark'`.
- Set `parent_id` to target folder id or `NULL`.
- Set `sort_order` to the start of the target folder.
- Set timestamps.
- Set `sync_state = 'dirty'`.

Add folder:

- Insert `item_type = 'folder'`.
- Same parent/order/sync behavior as bookmark.

Edit normal bookmark:

- Update `title` and `url`.
- Increment `revision`.
- Update `updated_at_utc`.
- Set `sync_state = 'dirty'`.

Edit folder:

- Update `title`.
- Increment `revision`.
- Update `updated_at_utc`.
- Set `sync_state = 'dirty'`.

Delete bookmark:

- Set `deleted_at_utc`.
- Increment `revision`.
- Set `sync_state = 'dirty'`.
- Hide from UI.

Delete folder:

- In one transaction, tombstone the folder and all descendants.
- Increment/update each affected row or use a clear sync tombstone strategy.
- Hide from UI.

Move item to folder start:

- Update `parent_id`.
- Update `sort_order`.
- Increment `revision`.
- Update `updated_at_utc`.
- Set `sync_state = 'dirty'`.

## Open Questions

These decisions can wait until implementation:

- Exact GUID string format.
- Exact `sort_order` allocation and rebalancing strategy.
- Whether secret folders should exist later.
- Exact encrypted payload JSON shape.
- Exact password change behavior.
- Exact WebDAV remote object format.
- Whether physical tombstone cleanup should be automatic or manual.
- Whether conflict copies should be visible in the normal tree or a separate conflict UI.

## Recommended Next Implementation Steps

1. Introduce domain/storage models that are not Avalonia view models.
2. Introduce repository interfaces for loading and saving tree items.
3. Add a SQLite storage implementation with explicit migrations.
4. Convert sample data into seed/test data behind the repository boundary.
5. Keep `BookmarkTreeService` or its successor as the mutation boundary.
6. Make add/edit/delete/move operations update storage metadata in one place.
7. Add tests around repository mapping and tree mutation persistence.
