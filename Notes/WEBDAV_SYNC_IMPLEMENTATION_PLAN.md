# WebDAV Sync Implementation Plan

This plan implements the sync architecture in
`Notes/WEBDAV_SYNC_ARCHITECTURE.md`.

It is a working plan for agents. Keep steps small, ask the user to run checks
after meaningful slices, and update this document if implementation changes the
architecture.

## Hard Rules

- Do not run `dotnet build`, `dotnet test`, `dotnet run`, or `dotnet format`.
  Ask the user to run them.
- Do not run `dotnet format` in any form.
- Do not log user data:
  - WebDAV full URLs;
  - usernames or passwords;
  - bookmark URLs;
  - bookmark titles;
  - folder names;
  - search queries;
  - local file paths;
  - source hashes;
  - object IDs if they can reveal persistent state;
  - secret generation IDs;
  - reset event IDs;
  - payload JSON;
  - encrypted/decrypted bytes;
  - salts, nonces, key material.
- Do not sync the SQLite database file.
- Sync item-level objects only.
- Treat conflicts conservatively.
- Process secret reset events before old secret data.

## Phase 0: Read Current Context

Read before editing:

- `AGENTS.md`
- `Notes/ARCHITECTURE.md`
- `Notes/PROJECT_STATE.md`
- `Notes/FUTURE_FEATURES_PLAN.md`
- `Notes/DATA_SCHEMA.md`
- `Notes/STORAGE_ARCHITECTURE.md`
- `Notes/ENCRYPTION_ARCHITECTURE.md`
- `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md`
- `Notes/SECRET_RESET_ARCHITECTURE.md`
- `Notes/WEBDAV_SYNC_ARCHITECTURE.md`
- `Storage/IBookmarkTreeStore.cs`
- SQLite stores under `Storage/Sqlite/`
- security services under `Security/`
- icon services under `IconProcessing/`
- settings services under `Settings/`
- logging service `Diagnostics/Logs.cs`

Confirm the current branch is clean or understand existing user changes before
editing.

## Phase 1: Add Sync Domain Models

Create a new folder:

```text
Sync/
```

Suggested records/enums:

```csharp
public enum SyncObjectKind
{
    Item,
    IconAsset,
    SecretIconAsset,
    CryptoProfile,
    SecretResetEvent,
    Device,
    Manifest
}

public enum SyncObjectState
{
    Clean,
    Dirty,
    Conflict,
    SyncError
}

public enum SyncBlockingReason
{
    None,
    PendingAsset,
    PendingCryptoProfile,
    InvalidRemoteObject,
    LocalOperationActive,
    RemoteUnavailable,
    WrongCredentials,
    UnsupportedRepositoryVersion
}

public sealed record SyncObjectIdentity(SyncObjectKind Kind, string Id);

public sealed record SyncRemoteObjectInfo(
    string RelativePath,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    long? ContentLength);

public sealed record SyncRunSummary(
    bool Succeeded,
    int DownloadedCount,
    int UploadedCount,
    int ConflictCount,
    int PendingAssetCount,
    int PendingCryptoProfileCount,
    int InvalidRemoteObjectCount,
    int ErrorCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
```

These domain enums do not have to map 1:1 to the existing
`BookmarkSyncState`. It is acceptable to keep database object dirtiness as
`clean` / `dirty` / `conflict` and store dependency problems in dedicated
pending/quarantine tables plus sync-run summary reason codes.

Add JSON DTOs for:

- manifest;
- item;
- regular icon asset;
- secret icon asset;
- crypto profile;
- secret reset event;
- device info.

Keep DTOs separate from storage records. Storage records can evolve without
forcing remote JSON changes.

Tests:

- DTOs serialize/deserialize;
- invalid `formatVersion` is rejected;
- missing required fields are rejected;
- secret item DTO cannot contain plaintext title/url.
- sync run summaries do not expose WebDAV URL, username, object payloads,
  source hashes, secret generation IDs, or persistent object IDs.

Ask the user to run:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

## Phase 2: Canonical Serialization And Content Hashing

Add a sync JSON serializer boundary.

Requirements:

- deterministic property order or deterministic canonical hash input;
- UTF-8 JSON;
- base64 for binary fields;
- no culture-dependent formatting;
- timestamps in UTC ISO-8601 round-trip format.

Suggested services:

```csharp
public interface ISyncJsonSerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(ReadOnlySpan<byte> utf8Json);
}

public interface ISyncContentHasher
{
    string ComputeHash(byte[] canonicalUtf8Json);
}
```

Tests:

- same logical object produces same hash;
- volatile ETag is not part of content hash;
- changing title/url/parent/sort/icon reference changes item hash;
- changing secret encrypted payload changes item hash without exposing
  plaintext.

## Phase 3: SQLite Migration For Sync Metadata

Add a migration for object categories that currently lack sync metadata.

Recommended columns:

- `icon_assets.sync_state`
- `icon_assets.remote_etag`
- `icon_assets.last_synced_at_utc`
- `icon_assets.content_hash`
- `icon_assets.modified_device_id`
- `secret_icon_assets.sync_state`
- `secret_icon_assets.remote_etag`
- `secret_icon_assets.last_synced_at_utc`
- `secret_icon_assets.content_hash`
- `secret_icon_assets.modified_device_id`
- `crypto_profiles.sync_state`
- `crypto_profiles.remote_etag`
- `crypto_profiles.last_synced_at_utc`
- `crypto_profiles.content_hash`
- `crypto_profiles.modified_device_id`

Existing `items` and `secret_reset_events` already have sync fields. Only change
them if implementation proves a missing field is required.

Add a pending asset reference table:

```sql
CREATE TABLE sync_pending_asset_refs (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    asset_kind TEXT NOT NULL CHECK (asset_kind IN ('regular-icon', 'secret-icon')),
    remote_asset_id TEXT NOT NULL,
    source_hash_algorithm TEXT NULL,
    source_hash TEXT NULL,
    created_at_utc TEXT NOT NULL,
    last_attempt_at_utc TEXT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error_code TEXT NULL,
    UNIQUE (item_id, asset_kind)
);
```

Purpose:

- allow bookmark/folder items to sync even if the referenced icon asset has not
  arrived yet;
- keep the intended remote icon reference instead of permanently replacing it
  with the default icon;
- retry icon resolution on later sync runs.

Add a deferred secret item table for secret items that arrive before their
matching crypto profile:

```sql
CREATE TABLE sync_deferred_secret_items (
    remote_item_id TEXT PRIMARY KEY,
    secret_generation_id TEXT NOT NULL,
    remote_etag TEXT NULL,
    content_hash TEXT NOT NULL,
    canonical_json BLOB NOT NULL,
    created_at_utc TEXT NOT NULL,
    last_attempt_at_utc TEXT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error_code TEXT NULL
);
```

Purpose:

- avoid inserting secret `items` rows with an invalid local `crypto_profile_id`;
- preserve encrypted remote secret item data until the profile arrives;
- keep sync retryable without asking the user to unlock secrets merely to store
  encrypted remote data.

Add a remote quarantine table:

```sql
CREATE TABLE sync_quarantined_remote_objects (
    id TEXT PRIMARY KEY,
    object_kind TEXT NOT NULL,
    relative_path TEXT NOT NULL,
    remote_etag TEXT NULL,
    content_hash TEXT NULL,
    reason_code TEXT NOT NULL,
    first_seen_at_utc TEXT NOT NULL,
    last_seen_at_utc TEXT NOT NULL,
    seen_count INTEGER NOT NULL DEFAULT 1
);
```

Purpose:

- remember invalid remote objects without applying them;
- avoid retrying the same broken object silently forever;
- show a non-sensitive invalid-object count in sync status.

Do not store raw invalid payload JSON in quarantine for v1.

Migration requirements:

- existing rows become `dirty`;
- `modified_device_id` must use current local device ID;
- no user data is logged.

Tests:

- migration applies to empty DB;
- migration applies to existing DB with icon assets, secret icon assets, and
  crypto profile;
- existing rows get sync metadata;
- existing app behavior remains unchanged.
- pending asset reference table exists and starts empty.
- deferred secret item table exists and starts empty.
- quarantined remote object table exists and starts empty.

## Phase 4: Local Sync Store Boundary

Create a local sync store abstraction.

Suggested interface:

```csharp
public interface ISyncLocalStore
{
    SyncLocalSnapshot LoadSnapshot();
    void ApplyRemoteChanges(SyncApplyBatch batch);
    void MarkUploaded(SyncObjectIdentity identity, string? remoteEtag, string contentHash, DateTimeOffset syncedAtUtc);
    void MarkConflict(SyncObjectIdentity identity, string reasonCode);
}
```

`SyncLocalSnapshot` should include:

- items, including tombstones;
- regular icon assets;
- secret icon assets;
- crypto profiles;
- secret reset events;
- pending asset refs;
- deferred secret items;
- quarantined remote object summaries;
- device/database metadata.

Implementation:

- use SQLite transactions;
- do not route this through Avalonia view models;
- for item records, reuse storage mapping helpers where possible;
- keep sync-specific mapping in `Sync/` or `Storage/Sqlite/Sync`.

Tests:

- snapshot includes dirty and clean objects;
- tombstoned normal items are included;
- reset events are included;
- secret payloads remain encrypted;
- pending icon refs are loaded/saved;
- deferred secret items are loaded/saved without plaintext;
- quarantine rows are loaded/saved without raw payloads;
- applying remote objects updates storage correctly;
- applying remote changes can request a later visible-tree reload.

## Phase 5: WebDAV Transport Abstraction

Create:

```text
Sync/WebDav/
```

Suggested interface:

```csharp
public interface IWebDavSyncTransport
{
    Task EnsureRepositoryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SyncRemoteObjectInfo>> ListAsync(string relativeDirectory, CancellationToken cancellationToken);
    Task<byte[]?> GetAsync(string relativePath, CancellationToken cancellationToken);
    Task<SyncPutResult> PutAsync(
        string relativePath,
        byte[] utf8Json,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken);
}
```

Implement a production `HttpClient` WebDAV transport:

- `MKCOL` for directories;
- `PROPFIND Depth: 1` for listing;
- `GET` for download;
- `PUT` for upload;
- conditional headers for optimistic concurrency;
- timeout/cancellation support.

Also implement a fake in-memory transport for tests.

Tests:

- fake transport create/list/get/put;
- conditional put success/failure;
- ETag update behavior;
- repository initialization creates expected directories.

Do not require a real WebDAV server for normal unit tests.

## Phase 6: Remote Repository Initialization

Implement a service that ensures:

```text
manifest.json
devices/
items/
icon-assets/
secret-icon-assets/
crypto-profiles/
secret-reset-events/
```

Behavior:

- if remote is empty, create manifest and directories;
- if manifest exists and version is supported, continue;
- if manifest exists and version is unsupported, return a user-facing error;
- if remote has unrelated files but no manifest, require explicit user choice
  later; v1 can fail safely.

Tests:

- empty remote initializes;
- existing supported manifest loads;
- unsupported manifest fails;
- manifest does not contain user data.

## Phase 7: Pull Pipeline

Implement remote download and apply ordering:

1. list/download reset events;
2. apply reset events;
3. list/download crypto profiles;
4. list/download regular icon assets;
5. list/download secret icon assets;
6. list/download items;
7. apply items after assets/profiles are available.

Reset event application:

- purge local secret data for the generation;
- delete active profile if matching;
- ignore later old secret objects for the same generation.

Item application:

- if local clean or missing, apply remote;
- if local dirty and remote same content, mark clean;
- if local dirty and remote different content, create/mark conflict.
- if an item references a missing regular/secret icon asset, apply the item,
  show default icon for now, and create/update a pending asset ref.
- if a secret item references a missing crypto profile, store it in
  `sync_deferred_secret_items` and do not insert an invalid `items` row.
- if a remote item graph creates a parent cycle, quarantine or recover affected
  objects instead of applying the cycle.

After downloading assets:

- resolve pending refs by remote asset id or source hash;
- update items to the resolved local asset id;
- remove resolved pending refs;
- request UI refresh if visible icons changed.

After downloading crypto profiles:

- retry deferred secret items for matching `secret_generation_id`;
- apply successfully resolved deferred items in a SQLite transaction;
- remove resolved deferred rows;
- delete deferred rows for generations that have a local reset event.

Invalid remote object handling:

- validate schema, format version, kind, required fields, content hash, secret
  plaintext rules, and parent graph before applying;
- write/update a quarantine row with a reason code for invalid objects;
- never log payload JSON or sensitive identifiers.

Tests:

- initial pull into empty DB;
- reset event wins over old secret item;
- secret item downloads without decrypting;
- secret item with missing crypto profile is deferred and hidden;
- later crypto profile download applies deferred secret item;
- reset event deletes matching deferred secret item;
- regular icon item reference resolves;
- regular icon missing during item pull creates pending ref and shows default;
- secret icon missing during item pull creates pending ref and shows default
  while visible;
- later regular icon download resolves pending ref;
- later secret icon download resolves pending ref without logging source hash;
- missing parent goes to recovery/conflict behavior;
- parent cycle is quarantined/recovered and never applied as a cycle;
- invalid JSON/schema/content hash creates quarantine row;
- local dirty vs remote changed marks conflict.

## Phase 8: Push Pipeline

Upload dirty objects in safe order:

1. dirty reset events;
2. dirty crypto profiles not invalidated by reset;
3. dirty regular icon assets;
4. dirty secret icon assets not invalidated by reset;
5. dirty items not invalidated by reset.

Rules:

- assets before items that reference them;
- profiles before secret items that reference them;
- reset events before old secret objects;
- use conditional PUT;
- if conditional PUT fails, download remote object and run conflict logic.
- if an asset upload succeeds but the referencing item upload fails, leave the
  item dirty and retry later. The remote orphan asset is harmless in v1.
- if a PUT succeeds but the app crashes before marking local state clean, the
  next sync must compare content hash/ETag and mark clean or detect conflict.

Tests:

- first push to empty remote;
- no-op second sync uploads nothing;
- dirty item uploads and becomes clean;
- dirty icon uploads before item;
- dirty secret icon uploads before secret item;
- asset upload success followed by item upload failure remains retryable;
- successful PUT followed by missing clean metadata is repaired by next sync;
- reset event prevents uploading purged old secret objects.

## Phase 9: Conflict Handling

Implement conservative v1 conflict rules.

Normal item conflicts:

- same content hash -> mark clean;
- edit/edit -> create conflict copy;
- delete/edit -> keep tombstone and preserve edited side as conflict copy;
- move/move -> conflict copy or conflict state;
- missing parent -> top-level recovered conflict.
- icon-reference conflict -> preserve both sides via conflict copy rather than
  silently choosing one icon.

Secret item conflicts:

- same content hash -> mark clean;
- if conflict copy requires decrypt/re-encrypt and session is unlocked, create
  encrypted conflict copy;
- if locked, mark conflict and require unlock/user attention.

Crypto profile conflicts:

- detect and report;
- do not silently overwrite.

Tests:

- edit/edit normal conflict preserves both;
- delete/edit normal conflict preserves edited data;
- icon-reference conflict preserves both icon choices;
- secret locked conflict does not overwrite;
- secret unlocked conflict copy decrypts/re-encrypts into a new item id, if the
  crypto/AAD implementation requires new ciphertext;
- crypto profile conflict is reported and local profile remains usable.

## Phase 10: Sync Application Service

Create a top-level orchestrator:

```csharp
public sealed class SyncApplicationService
{
    Task<SyncRunSummary> SyncNowAsync(CancellationToken cancellationToken);
}
```

Responsibilities:

- load settings/credentials;
- call repository initialization;
- run pull;
- run push;
- update last sync status;
- report whether local visible data changed;
- avoid concurrent sync runs with an in-process gate;
- avoid applying sync while local edit/write operations are active;
- log only non-sensitive summary.

Add a small local operation gate abstraction, for example:

```csharp
public interface ISyncOperationGate
{
    bool CanApplySync { get; }
    IDisposable EnterLocalWriteOperation();
    IDisposable EnterEditorSession();
}
```

V1 should postpone or refuse sync apply while any bookmark/folder editor,
icon-library selection, DnD operation, CRUD command, master-password change, or
master-password reset is active. The user-facing status should be non-fatal,
for example "sync postponed because an edit operation is in progress".

Tests:

- successful full run;
- remote unavailable;
- wrong credentials;
- conflict summary;
- sync now while editor session is active returns local-operation-active status
  and does not apply remote changes;
- local CRUD/DnD operation cannot run concurrently with sync apply;
- cancellation.

## Phase 11: Settings And Credentials UI

Add a `Sync` section to `SettingsDialog`.

Minimum UI:

- WebDAV URL;
- username;
- password for current run or credential-store-backed field;
- enabled checkbox;
- test connection button;
- sync now button;
- last successful sync text;
- status banner for errors/success.

Implementation guidance:

- keep content width-limited and centered like existing settings sections;
- use `StatusBanner`;
- do not close the settings dialog after sync/test connection;
- do not log entered URL/username/password;
- store URL/username in settings;
- keep password persistence behind `ISyncCredentialStore`.
- current service wiring uses `SyncApplicationServiceFactory`, which validates
  enabled state, WebDAV URL, username, and session credentials before creating a
  sync service;
- current credential implementation is `InMemorySyncCredentialStore`, so WebDAV
  password is not persisted across app restarts.

Ask the user before deciding whether v1 remembers WebDAV password on disk or
asks each app run.

## Phase 12: Main Window Refresh After Sync

After a successful sync that changed local data:

- reload visible tree from storage;
- preserve expanded folders where possible;
- rebuild search index from visible projection;
- clear or update search results consistently;
- clear decrypted secret icon cache only when secret session/profile/reset state
  changes.
- if only pending/quarantine state changed and visible bookmark data did not,
  update sync status without unnecessary tree rebuilds.

Tests:

- sync remote add appears in tree after reload;
- sync remote delete disappears;
- hidden secret remote add does not appear while locked;
- unlocked secret remote add appears after unlock/show;
- item with missing icon appears with default icon;
- when the pending icon arrives later, visible row updates to the custom icon;
- deferred secret item is invisible until the matching crypto profile arrives;
- quarantined invalid remote object does not affect visible tree;
- search reflects synced changes.

## Phase 13: Manual Regression Guide Update

Update `Personal/MANUAL_REGRESSION_GUIDE.md` with sync scenarios:

- first device initializes remote;
- second device pulls;
- normal CRUD sync;
- icon sync;
- item arrives before icon asset and later resolves;
- secret bookmark sync;
- secret icon sync;
- secret item waits for missing crypto profile and appears after profile arrives;
- master password change sync;
- master password reset sync;
- conflict scenarios;
- invalid remote object/quarantine scenario with a test fixture or fake
  transport;
- sync now while an editor dialog is open;
- wrong credentials;
- remote unavailable.

Also update:

- `Notes/ARCHITECTURE.md`;
- `Notes/PROJECT_STATE.md`;
- `Notes/FUTURE_FEATURES_PLAN.md`;
- `README.md` if user-facing setup commands/settings change.

## Phase 14: Suggested Manual Test Matrix

After implementation slices, ask the user to run:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

Manual test with two local app data directories and one WebDAV test folder:

1. Device A creates normal bookmark/folder/icon.
2. Device A syncs.
3. Device B pulls.
4. Device B edits/moves/deletes.
5. Device B syncs.
6. Device A pulls.
7. Repeat with secret bookmarks and encrypted icons.
8. Repeat with master-password reset.
9. Repeat with offline conflict edits.
10. Repeat with interrupted/partial sync using a fake or controlled WebDAV
    folder when possible.

## Phase 15: Deferred Work

Do not implement in v1 unless required:

- automatic periodic/background sync;
- remote garbage collection;
- multi-envelope crypto profile conflict handling;
- advanced user-facing pending/quarantine management UI;
- persistent credential storage through OS keychain;
- sync object compression;
- binary asset payload split;
- advanced conflict-resolution UI;
- server-specific WebDAV compatibility workarounds beyond basic robust HTTP
  handling.
