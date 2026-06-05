# Stranichnik Future Features Plan

This note is local agent context. It records the current thinking about the key future features that define why the project exists.

Important: this is not a fixed contract or irreversible architecture. These decisions are working hypotheses. They should guide future implementation, but they may change if development uncovers better approaches, technical constraints, security concerns, or simpler designs.

## Core Future Features

The project is not just a basic bookmark manager. Three future features are central:

1. Selective encryption for secret bookmarks.
2. Good bookmark search.
3. WebDAV-based synchronization without a custom backend.

These features must be considered together. They affect data model, storage design, search indexing, sync format, and UI behavior.

## High-Level Direction

Preferred long-term direction:

- Local SQLite database as the app's working storage.
- Application-level selective encryption for secret bookmark payloads. The first implementation may favor simplicity over maximum cryptographic sophistication.
- Search index as a separate concern, not hardcoded into UI. The first application integration uses the standalone in-memory `Stranichnik.Search` library.
- WebDAV sync as item-level sync, not whole SQLite file sync.
- Application services as the central place for add/edit/delete/move operations.

Avoid building UI operations that mutate view-model collections directly in ways that would be hard to mirror later to SQLite, search index, encryption, and sync metadata.

## Selective Encryption

### User Goal

Some bookmarks must be marked as secret.

Secret bookmarks:

- Must not be readable from the SQLite file in a hex editor.
- Must not be readable by opening the database and running SQL queries.
- Should be hidden from the UI while the app is locked.
- Should become visible only after the user explicitly unlocks secrets with a master password.
- Should require entering the password only once per app run, unless the user locks again.

It is acceptable that the database reveals that some secret rows exist. Hiding even the existence of secret records is a harder problem and is not the current target.

### Chosen Direction

Use application-level field/payload encryption, not whole-database encryption as the default design.

Reason:

- The user wants selective secrecy per bookmark.
- Normal bookmarks can remain searchable and readable without unlock.
- Secret bookmarks can be hidden entirely until unlock.
- Full database encryption would force all data behind the same unlock boundary and does not match the desired UX as well.

### Proposed Data Shape

Normal bookmark:

- `id`
- `parent_id`
- `is_secret = false`
- plaintext `title`
- plaintext `url`
- normal metadata

Secret bookmark:

- `id`
- `parent_id`
- `is_secret = true`
- technical metadata only
- `encrypted_payload`

The encrypted payload should contain the sensitive bookmark fields, for example:

- title
- url
- notes
- tags
- future searchable text

### Key Management Direction

The user's goal is practical privacy from casual inspection, not maximum cryptographic hardness against a highly motivated attacker.

Do not invent custom cryptography. Avoid weak "obfuscation" such as Base64-only storage, XOR, or homegrown ciphers.

Preferred first implementation:

1. Store a random salt in the database.
2. Derive an encryption key from the master password using a password KDF.
3. Encrypt each secret bookmark payload with that derived key.

This is simpler than a full key-wrapping design. The tradeoff is acceptable for the current goals:

- Changing the master password will require re-encrypting all secret bookmarks.
- There is no separate data key to rotate independently.
- The implementation is easier to understand and test.

Possible later upgrade:

1. Generate a random Data Encryption Key.
2. Derive a Key Encryption Key from the master password using a password KDF.
3. Encrypt/wrap the Data Encryption Key with the Key Encryption Key.
4. Encrypt secret bookmark payloads with the Data Encryption Key.

Benefits of the later upgrade:

- Changing the master password only requires re-wrapping the data key.
- Bookmark payloads do not all need to be re-encrypted on password change.
- The architecture follows common key-management practice.

### Crypto Direction

Likely choices:

- AES-GCM via .NET `AesGcm` for authenticated encryption.
- PBKDF2 is acceptable for the first implementation if it keeps dependencies and complexity low.
- Argon2id remains a possible later improvement if stronger password-based key derivation becomes important.

Do not invent custom cryptography.

### Search Interaction

Never write plaintext search index entries for secret bookmarks to disk.

More details are in the search section below.

## Search

### User Goal

Search should be better than simple substring matching.

The user wants to type an approximate remembered phrase or rough wording and get relevant bookmarks, even when the query does not exactly match the bookmark text.

Search is required only for bookmarks, not folders.

### Important Constraint

Search and encryption conflict.

A good search index wants to store searchable text. Secret bookmarks must not leak plaintext through a disk index.

### Chosen Direction

Keep search behind a service interface. Do not couple search to Avalonia view models.

Possible future interfaces:

- `ISearchService`
- `ISearchIndex`
- `BookmarkSearchDocument`

The search service should consume bookmark data from application/domain services or repository projections, not directly scrape UI controls.

Current preferred first approach:

- Build the entire search index in memory.
- Rebuild the index at app startup from non-secret bookmarks.
- When secrets are unlocked, decrypt secret bookmarks and add them to the in-memory index.
- When secrets are locked again or the app exits, discard secret search data.

This keeps the SQLite database and any files on disk free of plaintext search terms from secret bookmarks.

Expected tradeoffs:

- Startup and unlock may spend time rebuilding the index.
- Memory use grows with bookmark count.
- The implementation may be less powerful than a mature persistent search engine.
- For a bookmark manager, this is likely acceptable for a long time, even with many thousands of bookmarks.

### Practical Options

Option A: Custom in-memory index

- Good first implementation.
- No search index is written to disk.
- Secret bookmark search is naturally handled after unlock.
- Easy to rebuild after add/edit/delete.
- Can start with tokenization, normalization, simple fuzzy scoring, and weighted fields.
- May become harder to evolve if search requirements become much more advanced.

Option B: SQLite FTS5

- Useful if a persistent index becomes desirable.
- Works inside SQLite.
- Supports full-text search and ranking such as BM25.
- Simpler than Lucene.NET.
- Must not store plaintext secret terms on disk.

Option C: Lucene.NET

- Better long-term fit if search quality becomes a defining feature.
- Supports analyzers, BM25, fuzzy queries, weighted fields, and richer ranking.
- More moving parts and a separate index.
- Must be designed carefully so secret bookmark terms are not persisted in plaintext.

Current preference:

- Use the standalone custom in-memory `Stranichnik.Search` index for the first integrated version.
- Keep the architecture open enough to replace or supplement it with SQLite FTS5 or Lucene.NET later.

Detailed standalone search-library planning documents:

- `Notes/SEARCH_ENGINE_DESIGN.md`
- `Notes/SEARCH_ENGINE_ARCHITECTURE.md`
- `Notes/SEARCH_ENGINE_IMPLEMENTATION_PLAN.md`

Current application integration:

- Application-side search glue lives in `Searching/`.
- The main app rebuilds search from non-secret visible bookmark records.
- The main window shows search results in place of the bookmark tree while search is active.
- Results are mapped back to current bookmark view models by ID.
- Search quality can be tuned later without changing storage or UI boundaries.

### Secret Bookmark Search

For secret bookmarks:

- Do not store plaintext secret search index on disk.
- While locked, secret bookmarks are absent from search results.
- After unlock, decrypt secret bookmarks and index them in memory.
- When locked again or app exits, discard the in-memory secret index.

This is a reasonable balance between privacy and useful search.

Avoid searchable encryption unless there is a strong reason later. It is complex and easy to get wrong.

## Synchronization

### User Goal

The user does not want:

- Browser sync lock-in.
- A custom hosted backend.
- VPS/server maintenance.
- A hosted database.

The desired experience is similar to Joplin sync:

- User enters WebDAV URL, username, and password.
- Multiple desktop app instances synchronize through a folder in a cloud drive.

### Chosen Direction

Use WebDAV as a sync transport, but do not sync the SQLite database file directly.

Preferred model:

- SQLite remains local working storage.
- WebDAV stores sync objects/files.
- Sync is item-level, not database-file-level.

### Why Not Sync The SQLite File

Direct SQLite file sync is risky:

- SQLite may use sidecar files such as WAL/SHM depending on journal mode.
- Two devices can modify the database independently.
- WebDAV cannot merge SQLite-level changes.
- Conflict resolution at whole-file level is poor.
- Partial upload/download failures are harder to reason about.

### Proposed WebDAV Shape

Rough remote layout:

```text
/stranichnik/
  info.json
  items/
    <bookmark-id>.json
    <folder-id>.json
    <tombstone-id>.json
  locks/
```

Exact shape may change.

Each sync item should contain enough metadata for conflict detection and merge:

- `id`
- `type`
- `parent_id`
- `is_secret`
- `updated_at`
- `deleted_at` or tombstone marker
- `device_id`
- version/revision metadata
- payload or encrypted payload

### Conflict Direction

Initial conflict approach can be simple but explicit:

- Item-level last-write-wins may be acceptable for non-conflicting changes.
- If the same item changed independently on two devices, prefer creating a conflict copy over silently losing data.
- Deletions should use tombstones, not immediate remote disappearance.

Useful simplifying decision already made:

- The app does not support arbitrary manual ordering between siblings.
- New or moved items go to the start of the folder.

This reduces sync complexity because there is no per-folder order list to merge.

### Desynchronization Risk

Item-level WebDAV sync cannot completely remove sync risk. WebDAV is a file transport, not a transactional multi-device database.

Expected risks:

- Two devices edit the same bookmark independently.
- One device deletes an item while another edits it.
- Sync is interrupted midway.
- A WebDAV provider has unreliable or inconsistent ETag/date behavior.
- The user manually edits or deletes remote sync files.
- Device clocks differ.
- A future app version changes the sync object format.

Risk level:

- Full data loss can be made unlikely with careful design.
- Conflict copies, duplicates, or occasional manual conflict resolution remain realistic.
- This is an acceptable tradeoff for avoiding a custom hosted backend.

Risk reduction strategy:

- Stable IDs for every folder/bookmark.
- Tombstones for deletions.
- Idempotent sync operations.
- Conservative conflict handling: preserve both versions when unsure.
- Device IDs and revision metadata.
- Use WebDAV ETag/conditional writes where provider support is reliable.
- Avoid relying only on wall-clock timestamps.
- Keep the sync format versioned.

## How These Features Fit Together

The app should evolve toward this layered structure:

```text
Avalonia UI
  MainWindowViewModel
  dialogs later

Application services
  MainWindowViewModel now, possible BookmarkTreeApplicationService later
                            add/edit/delete/move
  SecretVaultService        lock/unlock/encrypt/decrypt
  SearchService             query/index/reindex
  SyncService               WebDAV orchestration

Storage
  IBookmarkTreeStore
  SqliteBookmarkTreeStore

Search
  ISearchIndex
  InMemorySearchIndex first
  SQLiteFtsSearchIndex or LuceneSearchIndex later if needed

Sync transport
  ISyncTarget
  WebDavSyncTarget

Crypto
  ISecretCryptoService
```

The exact names can change. The important idea is separation of responsibilities.

## Impact On Near-Term Work

Add/edit/delete/move operations are currently centralized in `MainWindowViewModel` and routed through `IBookmarkTreeStore`.

Near-term recommended direction:

1. Keep operations centralized.
2. Keep SQLite mutations behind `IBookmarkTreeStore`.
3. Keep operation result objects useful for UI updates and future hooks.
4. Consider extracting a `BookmarkTreeApplicationService` later if search/encryption/sync orchestration makes `MainWindowViewModel` too large.

This prepares the app for:

- SQLite store writes.
- Search index updates.
- Secret payload handling.
- Sync dirty flags and tombstones.

## Current Concrete Next Steps

Recommended next steps, still flexible:

1. Design selective secret bookmark support before writing code.
2. Implement the first selective encryption storage/model slice.
3. Add unlock/lock UI and hide locked secret bookmarks from tree/search.
4. Add WebDAV item-level sync after encryption data shapes are clear.

## References To Revisit

Useful areas to research again before implementation:

- OWASP Cryptographic Storage Cheat Sheet.
- OWASP Key Management Cheat Sheet.
- .NET `AesGcm`.
- PBKDF2 / `Rfc2898DeriveBytes`.
- Argon2id / libsodium password hashing as possible later improvement.
- SQLite FTS5.
- Lucene.NET.
- WebDAV RFC 4918, especially ETags and conditional writes.
- Joplin sync and encryption architecture notes.

Do not assume this note is complete security design. Before implementing encryption, do a fresh careful review.
