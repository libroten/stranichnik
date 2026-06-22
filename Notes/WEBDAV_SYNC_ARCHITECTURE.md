# WebDAV Sync Architecture

This document designs WebDAV-based synchronization for Stranichnik.

It is a working architecture, not a permanent contract. The project may change
the design if implementation exposes a simpler, safer, or more reliable path.
Do not implement sync by uploading the SQLite database file.

## Goals

The sync feature should let the user synchronize bookmarks between desktop
installations without running a custom backend.

Primary goals:

- use a user-provided WebDAV folder as the remote transport;
- keep SQLite as the local working database;
- sync logical objects, not the SQLite database file;
- preserve normal bookmarks, folders, regular icons, secret bookmarks,
  encrypted secret icons, crypto profile metadata, and master-password reset
  events;
- avoid silent data loss;
- handle offline edits from multiple devices conservatively;
- keep secret bookmark title/URL and secret icon processed bytes encrypted on
  disk and on WebDAV;
- never write plaintext search indexes to WebDAV;
- keep implementation testable without a real WebDAV server.

Secondary goals:

- start with manual sync or an explicit "sync now" command;
- add automatic/background sync only after the core engine is reliable;
- make the first implementation understandable rather than clever.

## Non-Goals For The First Version

The first sync version should not try to:

- sync the whole SQLite database file;
- provide real-time collaborative editing;
- implement perfect three-way semantic merge for every conflict;
- garbage-collect all remote tombstones/assets immediately;
- encrypt the entire WebDAV sync folder;
- hide that secret rows or secret icon rows exist;
- implement a persistent search index;
- depend on a server process or custom cloud service.

## Current Project Constraints

Current storage and privacy rules that sync must respect:

- the visible root folder is synthetic and must not be synced as user data;
- top-level items use `parent_id = NULL`;
- normal folders are plaintext;
- normal bookmarks store plaintext `title` and `url`;
- secret bookmarks store `title = NULL`, `url = NULL`,
  `encrypted_payload`, `encryption_nonce`, `crypto_profile_id`, and
  `secret_payload_format_version`;
- regular custom/favicons live in `icon_assets`;
- secret custom/favicons live in `secret_icon_assets` and store encrypted
  processed icon bytes;
- secret icon source hashes are intentionally plaintext for deduplication;
- secret master-password reset physically purges live secret bookmarks and
  secret icon assets, then stores one compact `secret_reset_event`;
- folders that only contained secret bookmark content are purged during reset;
- search index is in memory only and must be rebuilt from the visible local
  projection after sync changes;
- logs must not include bookmark URLs, titles, folder names, search queries,
  passwords, key material, source hashes, local file paths, sync object payloads,
  or secret generation IDs.

## High-Level Shape

```text
Avalonia UI
  -> Sync settings / Sync now command
    -> SyncApplicationService
      -> ISyncLocalStore
      -> IWebDavSyncTransport
      -> SyncEngine
        -> Local SQLite objects
        -> Remote WebDAV objects
```

SQLite remains the local source used by the app. WebDAV stores portable sync
objects. The sync engine compares local objects with remote objects and applies
changes into SQLite through sync-specific storage APIs.

The normal application `IBookmarkTreeStore` remains responsible for user
operations. Sync should not directly mutate Avalonia view models. After a sync
run changes local storage, `MainWindowViewModel` should reload the visible tree
and rebuild search from the same projected storage snapshot.

## Remote Directory Layout

Recommended remote base directory:

```text
<user-webdav-url>/stranichnik-sync-v1/
```

Inside it:

```text
manifest.json
devices/
  <device-id>.json
items/
  <item-id>.json
icon-assets/
  <icon-asset-id>.json
secret-icon-assets/
  <secret-icon-asset-id>.json
crypto-profiles/
  <secret-generation-id>.json
secret-reset-events/
  <secret-generation-id>.json
.tmp/
  <temporary-upload-id>.json
```

Notes:

- object IDs should be URL-safe application-generated identifiers;
- `items/<id>.json` represents one folder/bookmark row, including tombstones;
- regular icon asset objects contain processed PNG bytes as base64;
- secret icon asset objects contain encrypted processed icon bytes as base64;
- crypto profile object ID is `secret_generation_id`, not local
  `crypto_profiles.id`, because local profile IDs are SQLite-local integers;
- reset event object ID is also `secret_generation_id`, because there can be
  only one reset event per generation;
- `devices/<device-id>.json` is diagnostic/coordination metadata, not a
  conflict-resolution authority.
- `.tmp/` is a service directory used for safer create-only uploads. Temporary
  files in it are not sync objects and should be ignored by pull logic.

Use UTF-8 JSON for v1. The icon blobs are small normalized `64x64` PNGs, so
base64-in-JSON is acceptable for simplicity. If this becomes inefficient later,
asset metadata and binary payloads can be split into separate files.

## Manifest

`manifest.json` identifies the remote folder as a Stranichnik sync repository.

Suggested shape:

```json
{
  "schema": "stranichnik.sync.manifest",
  "formatVersion": 1,
  "repositoryId": "guid-or-ulid",
  "createdAtUtc": "2026-06-12T00:00:00Z",
  "createdByDeviceId": "device-id",
  "minimumAppSyncVersion": 1
}
```

Rules:

- create it when initializing an empty remote folder;
- never put user bookmark data into it;
- use it to detect wrong folders or incompatible future formats;
- if manifest exists with an unsupported version, refuse sync with a clear
  message.

## Object Metadata Conventions

Every sync object should include:

```json
{
  "schema": "stranichnik.sync.<type>",
  "formatVersion": 1,
  "id": "...",
  "updatedAtUtc": "...",
  "modifiedDeviceId": "...",
  "contentHash": "sha256:...",
  "deletedAtUtc": null
}
```

`contentHash`:

- computed from a canonical representation of the logical object content;
- computed while the `contentHash` field itself contains the stable placeholder
  value `sha256:pending`;
- excludes volatile transport metadata such as WebDAV ETag;
- helps detect no-op changes and conflict equivalence;
- must be verified when reading a remote object; if the stored hash does not
  match the canonical object bytes, the remote object is invalid and should be
  quarantined rather than applied;
- must not be logged.

Binary JSON fields use base64. Remote objects with malformed base64 payloads or
nonces are invalid remote objects and should be quarantined before any apply
logic attempts to decode them.

`modifiedDeviceId`:

- local device ID from app metadata;
- used only for diagnostics and deterministic tie-breakers;
- must not contain machine name or username.

## Item Sync Object

Remote item object for a folder:

```json
{
  "schema": "stranichnik.sync.item",
  "formatVersion": 1,
  "id": "item-id",
  "parentId": null,
  "kind": "folder",
  "sortOrder": 1000,
  "title": "Work",
  "url": null,
  "isSecret": false,
  "iconAssetRef": {
    "assetId": "regular-icon-asset-id",
    "sourceHashAlgorithm": "sha256",
    "sourceHash": "..."
  },
  "secretIconAssetRef": null,
  "encryptedPayload": null,
  "encryptionNonce": null,
  "cryptoProfileSecretGenerationId": null,
  "secretPayloadFormatVersion": null,
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "deletedAtUtc": null,
  "revision": 3,
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Remote item object for a normal bookmark:

```json
{
  "schema": "stranichnik.sync.item",
  "formatVersion": 1,
  "id": "item-id",
  "parentId": "folder-id-or-null",
  "kind": "bookmark",
  "sortOrder": 2000,
  "title": "Example",
  "url": "https://example.com/",
  "isSecret": false,
  "iconAssetRef": null,
  "secretIconAssetRef": null,
  "encryptedPayload": null,
  "encryptionNonce": null,
  "cryptoProfileSecretGenerationId": null,
  "secretPayloadFormatVersion": null,
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "deletedAtUtc": null,
  "revision": 5,
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Remote item object for a secret bookmark:

```json
{
  "schema": "stranichnik.sync.item",
  "formatVersion": 1,
  "id": "item-id",
  "parentId": "folder-id-or-null",
  "kind": "bookmark",
  "sortOrder": 3000,
  "title": null,
  "url": null,
  "isSecret": true,
  "iconAssetRef": null,
  "secretIconAssetRef": {
    "assetId": "secret-icon-asset-id",
    "sourceHashAlgorithm": "sha256",
    "sourceHash": "..."
  },
  "encryptedPayload": "base64",
  "encryptionNonce": "base64",
  "cryptoProfileSecretGenerationId": "secret-generation-id",
  "secretPayloadFormatVersion": 1,
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "deletedAtUtc": null,
  "revision": 2,
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Rules:

- never put secret bookmark title or URL into a remote item object;
- `cryptoProfileSecretGenerationId` is remote-stable and maps to local
  `crypto_profiles.secret_generation_id`;
- local integer `crypto_profiles.id` must not be used remotely;
- local secret item rows persist `items.secret_generation_id`; push uses this
  item-level generation for `cryptoProfileSecretGenerationId` and only uses the
  local crypto profile as a matching dependency check;
- item tombstones keep enough metadata to propagate deletion, but should not
  keep secret encrypted payloads after a master-password reset;
- normal delete tombstones may keep previous plaintext fields in v1, because
  the data was already normal/plaintext. If a future "secure delete normal
  bookmark" feature appears, this must be revisited.

## Regular Icon Asset Object

```json
{
  "schema": "stranichnik.sync.iconAsset",
  "formatVersion": 1,
  "id": "icon-asset-id",
  "sourceHashAlgorithm": "sha256",
  "sourceHash": "...",
  "sourceSizeBytes": 1234,
  "processedMimeType": "image/png",
  "processedWidth": 64,
  "processedHeight": 64,
  "processedBytes": "base64",
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Rules:

- assets are immutable;
- if the same `sourceHash` already exists locally under another asset ID, reuse
  the local asset when applying item references;
- do not log source hash or processed bytes;
- orphan remote assets may remain in v1.

## Secret Icon Asset Object

```json
{
  "schema": "stranichnik.sync.secretIconAsset",
  "formatVersion": 1,
  "id": "secret-icon-asset-id",
  "sourceHashAlgorithm": "sha256",
  "sourceHash": "...",
  "sourceSizeBytes": 1234,
  "processedMimeType": "image/png",
  "processedWidth": 64,
  "processedHeight": 64,
  "encryptedProcessedBytes": "base64",
  "encryptionNonce": "base64",
  "payloadFormatVersion": 1,
  "secretGenerationId": "secret-generation-id",
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Rules:

- encrypted bytes stay encrypted on WebDAV;
- accepted plaintext metadata leaks are the same as local
  `secret_icon_assets`;
- a reset event for `secretGenerationId` makes this object obsolete;
- devices that cannot unlock secrets may still store and upload/download these
  encrypted blobs without decrypting them.

## Crypto Profile Object

The current local profile table uses local integer `id`. Remote sync should key
the profile by `secret_generation_id`.

Suggested v1 shape:

```json
{
  "schema": "stranichnik.sync.cryptoProfile",
  "formatVersion": 1,
  "secretGenerationId": "secret-generation-id",
  "profileVersion": 1,
  "kdfName": "pbkdf2",
  "kdfHashAlgorithm": "sha256",
  "kdfIterations": 600000,
  "kdfSalt": "base64",
  "kekLengthBytes": 32,
  "dataKeyAlgorithm": "aes-256-gcm",
  "wrappedDataKey": "base64",
  "wrappedDataKeyNonce": "base64",
  "encryptionAlgorithm": "aes-256-gcm",
  "payloadFormat": "json-v1",
  "passwordCheckPayload": "base64",
  "passwordCheckNonce": "base64",
  "createdAtUtc": "...",
  "updatedAtUtc": "...",
  "modifiedDeviceId": "...",
  "contentHash": "sha256:..."
}
```

Rules:

- do not log the generation ID, salts, nonces, wrapped key, or password-check
  payload;
- a password change updates this object by rewrapping the same DEK;
- secret bookmark payload objects do not need to be rewritten after password
  change;
- if two devices independently change the master password offline, the first
  implementation should treat the crypto profile as a conflict-prone object.

### Password-Change Conflict Policy

Offline simultaneous password changes are rare but important.

First-version policy:

- detect a crypto profile conflict if local profile is dirty and remote profile
  ETag/content changed;
- do not silently overwrite;
- keep the local profile dirty/conflicted and show a sync error requiring user
  attention;
- do not apply a remote profile change that would make local secret data
  impossible to unlock without a clear user-facing result.

Possible future improvement:

- introduce multiple wrapped-DEK envelopes per secret generation and an active
  envelope marker. That would preserve multiple valid password wrappers during
  conflicts, but it also complicates password-change semantics because old
  passwords may remain valid until explicitly retired.

Do not implement the multi-envelope model in v1 unless the user explicitly
chooses that complexity.

## Secret Reset Event Object

```json
{
  "schema": "stranichnik.sync.secretResetEvent",
  "formatVersion": 1,
  "secretGenerationId": "secret-generation-id",
  "resetAtUtc": "...",
  "resetDeviceId": "device-id",
  "createdAtUtc": "...",
  "modifiedDeviceId": "device-id",
  "contentHash": "sha256:..."
}
```

Meaning:

```text
All secret bookmarks and secret icon assets from this generation were
intentionally discarded.
```

Rules:

- reset event wins over old secret bookmark objects from the same generation;
- reset event wins over old secret icon asset objects from the same generation;
- reset event deletes the active local crypto profile if it belongs to that
  generation;
- reset event must be processed before item/icon/profile objects during pull;
- reset event does not contain deleted payloads;
- reset event should not be garbage-collected in v1.

## Local SQLite Sync Metadata

Existing `items` and `secret_reset_events` already have sync-related fields.
To sync all object kinds, add sync metadata to missing categories or introduce a
central sync state table.

Recommended v1 approach:

- keep existing item sync columns;
- keep existing reset-event sync columns;
- add sync metadata columns to `icon_assets`, `secret_icon_assets`, and
  `crypto_profiles`:

```text
sync_state TEXT NOT NULL DEFAULT 'dirty'
remote_etag TEXT NULL
last_synced_at_utc TEXT NULL
content_hash TEXT NULL
modified_device_id TEXT NOT NULL
```

Reason:

- it is explicit and easy to inspect in SQLite;
- it avoids a second mapping table for the first implementation;
- object-specific repositories can update their own sync metadata.

Alternative:

- one `sync_object_states` table keyed by `(object_type, object_id)`.

That central table becomes attractive if sync metadata grows, but it adds more
joins and indirection. Prefer direct columns for v1.

## WebDAV Transport

Create a transport abstraction rather than scattering HTTP calls.

Suggested interface:

```csharp
public interface IWebDavSyncTransport
{
    SyncRemoteObjectList ListAsync(string relativeDirectory, CancellationToken cancellationToken);
    Task<RemoteObjectContent?> GetAsync(string relativePath, CancellationToken cancellationToken);
    Task<PutRemoteObjectResult> PutAsync(
        string relativePath,
        byte[] utf8Json,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken);
    Task EnsureDirectoryAsync(string relativeDirectory, CancellationToken cancellationToken);
}
```

Implementation details:

- use `HttpClient`;
- use `MKCOL` to create directories;
- use `PROPFIND` with `Depth: 1` to list objects;
- use `GET` to download objects;
- use `PUT` to upload objects;
- use conditional requests:
  - `If-None-Match: *` for create-only uploads when supported;
  - `If-Match: <etag>` for updating known remote objects;
- store returned ETags exactly as the server returns them;
- treat HTTP 412/409 as sync conflicts, not fatal crashes;
- use request timeouts and cancellation tokens;
- do not perform network I/O on the UI thread.

Some WebDAV servers have imperfect ETag behavior. Therefore:

- use ETag as the primary remote-change signal;
- also keep object `contentHash`;
- if ETag changes but content hash is the same, treat as no-op and refresh the
  stored ETag.

## Credentials And Settings

Recommended settings:

```text
sync.webDavUrl
sync.username
sync.credentialStorageKind
sync.repositoryId
sync.lastSyncAtUtc
```

Credential storage:

- do not log WebDAV URL, username, or password;
- do not store password in SQLite;
- prefer an `ISyncCredentialStore` abstraction;
- current implementation uses `PersistentSyncCredentialStore`;
- the credential store keeps a session cache first;
- when the user enters a WebDAV password and saves sync settings, it tries the OS credential store first:
  macOS Keychain, Windows Credential Manager, or Linux Secret Service via
  `secret-tool`;
- if the OS credential store is unavailable, the user can explicitly accept an
  obfuscated local fallback file;
- the fallback file stores only a username-hash plus XOR(username, password)
  bytes encoded as Base64. This is a convenience fallback and not strong
  cryptographic protection;
- when fallback file storage is active, settings must show a persistent warning.
- the sync settings reset action must clear WebDAV URL, username, credential
  backend metadata, last successful sync timestamp, in-memory credentials, and
  persisted credentials for the saved username.
- `--simulate-unavailable-system-credential-store` forces the OS credential
  backend to be unavailable and exists only to test this fallback path.

Do not block the sync engine on perfect credential persistence. Keep credential
storage behind an interface.

## Sync Run Algorithm

### High-Level Run

```text
1. Load sync settings and credentials.
2. Ensure remote repository directories exist.
3. Load local syncable objects and dirty states.
4. List remote object directories and ETags.
5. Download remote objects that are new or have changed ETags.
6. Apply remote reset events first.
7. Apply remote crypto profiles, except profiles from reset secret generations.
8. Apply remote icon assets.
9. Apply remote secret icon assets.
10. Apply remote items.
11. Upload local dirty reset events.
12. Upload local dirty crypto profiles.
13. Upload local dirty icon assets.
14. Upload local dirty secret icon assets.
15. Upload local dirty items, with parent folders before children.
16. Reload visible tree/search if local storage changed.
17. Record sync result/status.
```

Remote repository initialization creates all required category directories,
including `.tmp/`. A connection test must not create these directories; it
should only check that the target WebDAV location is reachable and readable
enough for sync setup.

Pull reset events first because they invalidate old secret data. Upload reset
events before uploading old dirty secret objects from the same generation.
When a reset event for generation `G` exists locally or arrives remotely during
the current pull, remote crypto profiles, secret icon assets, and secret items
from generation `G` must be ignored. Reset wins over old secret objects even if
those old objects are still present on the WebDAV server.

## Reliability Model

WebDAV sync is intentionally an eventually-consistent object sync, not a remote
transaction system. A sync run may stop after any individual HTTP request or
after any local SQLite write. The design must make every step safe to retry.

Core reliability rules:

- every remote object is independently valid;
- every local apply step is performed inside SQLite transactions;
- a local object is marked clean only after its corresponding remote operation
  and local metadata update both succeed;
- applying the same remote object twice must be a no-op or an equivalent safe
  update;
- uploading the same logical local object twice must either be a no-op or create
  a detected conflict through ETag/content-hash checks;
- missing referenced objects are represented as pending sync state, not as data
  loss;
- invalid remote objects are quarantined or ignored with a non-sensitive error,
  not partially applied.
- item upload dependencies are enforced: if uploading a parent folder fails,
  descendants from the same push plan must not be uploaded in that run.
- a clean local object that was previously synced but disappeared from remote is
  marked dirty and uploaded again instead of being treated as locally deleted.
- create-only uploads are made safer by writing a temporary object under
  `.tmp/` and moving it to the final path with WebDAV `MOVE` when the provider
  supports that operation.
- the production WebDAV transport performs best-effort cleanup of stale `.tmp/`
  objects during repository initialization. Fresh temp files are skipped.

This means the app may temporarily show incomplete state, for example a bookmark
with the default icon while the custom icon asset is still pending. That is
acceptable. Losing the intended icon reference, overwriting user content, or
making secret data undecryptable is not acceptable.

Current implementation note:

- pull application is centralized in `ISyncLocalStore.ApplyPullPlan(...)`;
- this keeps remote apply, matched-dirty remote metadata refresh, conflict state, quarantine
  state, and missing-remote dirty marks in one storage boundary;
- the current SQLite implementation applies the whole pull plan through one
  shared `SqliteConnection`/`SqliteTransaction`, including remote object apply,
  matched-dirty remote metadata refresh, conflict state, quarantine state,
  missing-remote dirty marking, pending icon refs, deferred secret items, reset
  events, icon assets, crypto profiles, and items;
- if local apply fails in the middle of a pull plan, SQLite rolls back the
  local batch so the next sync can retry from the previous consistent state.
- push remains object-by-object because WebDAV has no repository-wide
  transaction.

## Sync Coordination And Local Operation Gate

Sync must not apply remote mutations while the user is in the middle of a local
operation that edits the same logical data.

V1 should block or postpone applying sync while any of these are active:

- bookmark editor dialog;
- folder editor dialog;
- icon library dialog launched from an editor;
- drag-and-drop operation;
- local add/edit/delete/move command;
- master-password change or reset flow.

The simplest implementation is an in-process gate:

```text
if local write/edit session is active:
  do not start sync apply
  show "sync postponed" or keep sync idle
```

Manual "Sync now" may still be clicked while a dialog is open, but the sync
service should return a non-destructive status telling the UI that sync cannot
run until the active edit operation is finished.

This rule prevents a common race:

1. User opens edit dialog for bookmark A.
2. Remote sync changes bookmark A.
3. User saves the old dialog state.
4. The save overwrites remote changes without a visible conflict.

Later versions may replace this coarse gate with optimistic concurrency checks
on item revision/content hash. V1 should prefer the simpler and safer gate.

## Sync Status Model

The existing item-level `sync_state` values (`clean`, `dirty`, `conflict`) are
not enough to describe every temporary sync condition. Some states are not
content conflicts. They are incomplete dependencies or transport problems.

Recommended v1 model:

- keep object dirtiness as a simple primary state:
  - `clean`;
  - `dirty`;
  - `conflict`;
- represent dependency problems in dedicated tables:
  - pending icon asset references;
  - pending crypto-profile references or deferred secret items;
  - quarantined invalid remote objects;
- expose a sync-run summary with non-sensitive reason codes:
  - `remote-unavailable`;
  - `wrong-credentials`;
  - `unsupported-repository-version`;
  - `conflicts-detected`;
  - `pending-assets`;
  - `pending-crypto-profiles`;
  - `local-operation-active`;
  - `invalid-remote-objects`.

Do not store or display raw remote payloads, URLs, titles, source hashes, secret
generation IDs, or persistent object IDs in normal UI. The UI can show counts by
category and human-readable actions such as "unlock secrets and sync again".

### Pull Object Decision Matrix

For each remote object:

```text
local missing, remote exists:
  apply remote object locally

local clean, remote content changed:
  apply remote object locally

local dirty, remote missing:
  keep local dirty; upload later

local dirty, remote same content:
  keep local dirty; refresh remote ETag/content hash metadata so push can update
  the existing remote object safely

local dirty, remote different content:
  conflict

local conflict:
  do not auto-overwrite unless a conflict-resolution path says so
```

For local clean objects:

```text
local clean + last_synced_at_utc != NULL + remote missing:
  mark local dirty, clear stale remote_etag, restore it during push

local clean + last_synced_at_utc == NULL + remote missing:
  do not treat as a deletion; it is a never-confirmed local object and should
  be handled by first-upload push rules

local clean + remote object exists but is invalid/quarantined:
  do not mark missing; keep the quarantine/problem path so the user can decide
  whether to clear or delete the remote file
```

### Push Object Decision Matrix

For each local object that should be pushed.

Push candidates are:

- objects with `sync_state = dirty`;
- objects with `last_synced_at_utc = NULL`, even if their `sync_state` is
  `clean`.

Referenced dependencies are not push candidates merely because a selected item
points to them. A dirty item can reference a clean already-synced parent folder,
regular icon asset, secret icon asset, or crypto profile without re-uploading
that dependency. If such a dependency really disappeared from WebDAV, the pull
phase marks that dependency dirty first, and then the normal dirty-object rule
uploads it again.

This distinction is important for WebDAV providers that do not always return
stable ETags. A clean synced dependency with `remote_etag = NULL` still counts
as known remote content when `last_synced_at_utc != NULL`; pushing it again as
create-only could create a false conflict and block the item that only needed
that dependency.

Important distinction:

```text
clean means "no local unsynced edit is pending"
clean + last_synced_at_utc != NULL means "known to be represented remotely"
clean + last_synced_at_utc == NULL means "not dirty, but never confirmed remote"
```

This matters for generated seed/sample data and any other locally-created object
that starts as clean before its first sync. A first push to an empty WebDAV
repository must still upload those objects, otherwise a later fresh database
cannot restore the complete tree.

Matched-dirty pull results are not uploads. They only prove that the current
remote object still matches the local object's last synced revision. Applying a
matched-dirty result may refresh `remote_etag`, `last_synced_at_utc`, and
`content_hash`, but it must keep `sync_state = dirty` so the following push phase
still uploads the local edit. This is especially important for crypto profiles:
after a master-password change, marking the matched dirty profile clean before
push would leave the old password wrapper on WebDAV.

For each selected local object:

```text
remote missing:
  PUT create-only

remote exists and stored remote_etag matches current remote ETag:
  PUT with If-Match

remote exists but ETag changed:
  download remote and run conflict logic

PUT succeeds:
  store returned ETag, last_synced_at_utc, content_hash, sync_state = clean

PUT conditional failure:
  download remote and run conflict logic
```

### Upload Atomicity

For create-only object upload, the production WebDAV transport uses a safer
two-step write when possible:

```text
PUT .tmp/<temporary-name>.json with If-None-Match: *
MOVE .tmp/<temporary-name>.json -> <final-object-path> with Overwrite: F
```

If the process or network fails during the temp `PUT`, the final object is not
touched. If it fails after the temp `PUT` but before/during `MOVE`, the remote
may contain an orphan temp file. Pull ignores `.tmp/`; repository
initialization performs best-effort cleanup of stale temp files after a
conservative retention period.

If the WebDAV provider does not support `MOVE`, the transport falls back to a
direct create-only `PUT` to the final path. This is less robust, but keeps sync
usable with simpler WebDAV servers.

Update uploads with an expected ETag currently remain direct conditional
`PUT`s. Do not change them to temp+MOVE without separate provider testing,
because WebDAV support for conditional destination replacement during `MOVE` is
not consistent enough to assume for v1.

## Conflict Strategy

Default rule:

```text
Preserve user data. Do not silently discard local or remote content.
```

First-version policy:

```text
If both sides made meaningful different user changes, do not auto-pick a
winner. Keep one side under the original identity and preserve the other side as
a conflict copy or explicit conflict state.
```

Last-write-wins is allowed only for clearly non-conflicting metadata refreshes
or identical content hashes. It is not the default for user data.

### Normal Item Conflicts

If a normal bookmark/folder changed locally and remotely:

- if canonical content hashes are equal, mark clean;
- if only one side changed non-overlapping technical metadata, apply safe merge;
- otherwise create a conflict copy for one side.

First-version conflict resolution is intentionally conservative. The sync engine
should not ask "which side is probably better" when meaningful user content
differs. It should preserve both sides and make the conflict visible/recoverable.

Meaningful user content includes:

- folder title;
- bookmark title;
- bookmark URL;
- parent/folder placement;
- deleted vs edited state;
- regular icon reference;
- secret icon reference;
- encrypted secret payload bytes.

Recommended v1 conflict copy:

- keep the local item with its original ID;
- insert the remote conflicting item as a new local item with a new ID;
- place the conflict copy under the same parent if possible, otherwise top-level;
- mark the copied item `sync_state = dirty`;
- append a localized suffix in UI-facing title when safe:
  - normal folder/bookmark: `" (sync conflict)"`
  - do not log the title;
- if parent is missing/deleted, place under top-level root and mark conflict.

The copied conflict item must be a normal local item with its own new stable ID.
It must not reuse the remote object's ID, because the original ID is already the
identity under conflict.

### Delete Vs Edit

If one side tombstoned an item and the other side edited it:

- preserve the edited content as a conflict copy;
- keep the tombstone for the original item ID;
- this avoids silently resurrecting deleted content or silently dropping an edit.

This means a user may see a conflict copy after sync. That is acceptable in v1:
the app preserved data instead of guessing whether deletion or edit should win.

### Move Vs Move

If both sides moved the same item to different parents:

- prefer conflict copy rather than choosing one parent silently;
- if content is otherwise equal and one parent is missing, move to top-level
  with conflict state.

### Secret Bookmark Conflicts

Secret conflicts are harder because encrypted payload AAD may bind ciphertext to
the item ID, and conflict copies may require decrypt/re-encrypt.

Recommended v1:

- if local/remote secret conflict can be resolved by content hash equality, mark
  clean;
- if a conflict copy is needed and the runtime secret session is unlocked for
  that generation, decrypt/re-encrypt into a new conflict item ID;
- if the session is locked, mark the item as conflict and do not overwrite local
  or remote content;
- show a non-sensitive sync status telling the user secrets must be unlocked to
  resolve a secret sync conflict.

Do not create a secret conflict copy by simply duplicating ciphertext under a
new item ID unless the crypto AAD design explicitly supports it.

### Icon Asset Conflicts

Icon assets are immutable and use GUID-like IDs, so same-ID different-content
conflicts should be extremely rare.

Policy:

- if same source hash/content hash, treat as same asset;
- if same ID but different hash/content, mark sync conflict and preserve both by
  creating a new local asset ID for the incoming object only if item references
  can be remapped safely;
- otherwise stop and report a non-sensitive sync error.

### Crypto Profile Conflicts

Crypto profile conflicts should not be automatically overwritten.

Policy:

- detect and report;
- keep local profile usable;
- do not apply a remote profile that would make local secret data unexpectedly
  inaccessible;
- require a later explicit conflict-resolution UI or user-guided recovery.

The first implementation should report this as a sync conflict/error and leave
the local profile usable. It is better to pause sync for the secret generation
than to make secret bookmarks impossible to unlock.

## Missing Referenced Assets

Items and assets are separate remote objects. A sync run can be interrupted, a
server can return partial results, or an old device can upload an item before
uploading its icon asset. Therefore item application must tolerate missing icon
assets.

Rule:

```text
Missing icon assets must not block applying bookmarks or folders.
```

### Regular Missing Icon Asset

If a normal bookmark/folder references a regular icon asset that is not available
locally and was not downloaded in the current sync run:

1. Apply the item locally.
2. Preserve the fact that it wants the missing remote icon.
3. Show the default icon in UI until the asset arrives.
4. Retry downloading/resolving the icon on later sync runs.

Do not simply replace the item reference with `NULL` and forget the intended
icon. That would permanently lose the icon assignment.

### Secret Missing Icon Asset

If a secret bookmark references a secret icon asset that is not available
locally and was not downloaded in the current sync run:

1. Apply the secret bookmark locally.
2. Preserve the pending secret icon reference.
3. When secrets are visible, show the default bookmark icon until the encrypted
   secret icon asset arrives and can be decrypted.
4. Retry downloading/resolving the secret icon on later sync runs.

Do not require the secret session to be unlocked merely to store the pending
reference. The encrypted bookmark payload and remote secret icon reference are
already sync metadata.

### Pending Reference Storage

Recommended v1 SQLite shape:

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

Notes:

- `remote_asset_id` is the ID from the sync object reference.
- `source_hash` is optional because the item reference may contain it. It must
  not be logged.
- if the missing asset later arrives under a different local ID because of
  deduplication, resolve by source hash when possible;
- after resolving, update the item to the local asset ID and remove the pending
  row;
- pending refs should survive app restart.

Alternative:

- store pending remote refs directly in nullable columns on `items`.

The separate table is preferred for v1 because it avoids complicating normal
runtime icon lookup and keeps "temporarily incomplete sync" state explicit.

### Applying Items With Missing Assets

When applying a remote item:

1. Try to resolve `iconAssetRef` / `secretIconAssetRef` to a local asset.
2. If resolved, assign the local `icon_asset_id` or `secret_icon_asset_id`.
3. If not resolved, set the local asset reference to `NULL` for runtime display
   and create/update `sync_pending_asset_refs`.
4. Mark the item clean only if the item content itself applied successfully.
   The pending asset ref remains a separate sync task.

This means a bookmark can be synced and visible while its icon is still pending.

### Later Asset Arrival

When a regular or secret icon asset is downloaded:

1. Store or deduplicate the asset locally.
2. Look for pending refs with matching `remote_asset_id` or source hash.
3. Update affected items to point to the resolved local asset ID.
4. Remove resolved pending refs.
5. Request visible tree/search refresh if the affected item is visible.

Search does not depend on icons, so only visual refresh is needed.

The local apply layer must also retry this resolution after every completed
pull plan, even if the asset was not part of the current remote apply batch.
This protects against interruptions after the asset was stored but before
pending refs were resolved.

## Missing Crypto Profiles And Deferred Secret Items

Secret bookmark items reference a remote crypto profile by
`cryptoProfileSecretGenerationId`. Locally, however, secret item rows reference a
local integer `crypto_profiles.id`. Therefore a secret item cannot be safely
inserted as a normal local secret bookmark until the matching crypto profile has
been applied locally.

Pull order downloads crypto profiles before items, but remote state can still be
incomplete:

- a sync run can be interrupted;
- a WebDAV server can return partial listings;
- a stale device can upload a secret item before its crypto profile;
- a user can manually delete a remote crypto-profile file;
- a reset event can invalidate the generation while old objects are still
  present.

Rule:

```text
A missing crypto profile must not make sync fail destructively, and must not
force the user to unlock secrets merely to store encrypted remote data.
```

Recommended v1 behavior:

1. If a remote secret item references a generation that has a local reset event,
   ignore the item because the reset wins.
2. If the matching crypto profile exists locally, apply the item normally.
3. If the matching crypto profile is missing, do not insert the item into
   `items` with an invalid `crypto_profile_id`.
4. Store the remote secret item in a deferred sync table.
5. Show a non-sensitive sync status such as "some secret data is waiting for
   missing crypto metadata".
6. Retry applying deferred secret items after later sync runs download the
   missing crypto profile.

Suggested deferred table:

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

Notes:

- `canonical_json` is the remote secret item JSON bytes. It contains encrypted
  payload only and must not contain plaintext title or URL.
- Do not log `remote_item_id`, `secret_generation_id`, `content_hash`, or the
  JSON bytes.
- If the matching reset event arrives later, delete matching deferred rows.
- If the crypto profile arrives later, apply deferred items in a SQLite
  transaction and remove successfully applied deferred rows.
- The local apply layer must retry deferred secret item application after every
  completed pull plan when the matching profile is already available locally.
  This protects against interruptions after a profile was stored but before
  deferred rows were applied.

Secret icon assets do not need the local integer profile ID merely to be stored,
because they already carry encrypted bytes and `secret_generation_id`. They may
be downloaded and stored while locked. They simply cannot be decrypted for
preview/display until the matching profile and runtime secret key are available.

## Secret Reset Precedence

When applying a reset event for generation `G`:

1. Store/apply the reset event locally.
2. Physically purge local secret bookmark rows for generation `G`.
3. Physically purge local `secret_icon_assets` for generation `G`.
4. Purge folders that only contained purged secret content.
5. Delete local active crypto profile if it belongs to `G`.
6. Lock/mark the runtime secret session not configured if the active profile was
   deleted.
7. Ignore future remote secret item/icon/profile objects for `G`.

If a stale device later uploads old secret objects for generation `G`, other
devices must ignore them because the reset event wins.

Remote cleanup of obsolete secret objects can be a later maintenance feature.

## Ordering And Parent Handling

The current UI only supports insert/move-to-folder-start.

Sync rules:

- sync `sort_order` as stored;
- order siblings by `sort_order DESC, id`;
- if two devices create equal `sort_order`, deterministic ID tie-breaker is OK;
- do not renumber remotely during v1 sync unless a local operation already needs
  rebalancing.

If an item references a missing parent:

- if the parent is expected to arrive later in the same sync run, delay applying
  that item until after all items are loaded;
- if the parent is tombstoned/missing after the run, place the item at top level
  and mark it as conflict/recovered;
- for secret items, preserve encrypted payload and keep hidden while locked.

Parent validation must also reject cycles. A remote item graph that would make a
folder its own ancestor is invalid. Do not apply such a graph directly. Put the
affected objects into conflict/recovered state or quarantine the invalid remote
objects with a non-sensitive reason code.

## Invalid Remote Objects And Quarantine

Remote data must be treated as untrusted input. Objects can be corrupted by
network issues, provider bugs, manual user edits, old app versions, or app bugs.

Reject or quarantine remote objects when:

- JSON is malformed;
- `schema` or `formatVersion` is unsupported;
- required fields are missing;
- object kind does not match directory;
- normal bookmark contains invalid secret-only fields;
- secret bookmark contains plaintext `title` or `url`;
- item parent graph creates a cycle;
- object references a reset secret generation;
- content hash does not match the canonical payload;
- same object ID has incompatible object kind.

Suggested quarantine table:

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

Do not store raw payload JSON in this table for v1. Keeping non-sensitive
metadata is enough to avoid retrying the same broken object silently forever and
to show a count in the UI.

Quarantine rows are also used to distinguish a new or changed problem from an
old unchanged problem:

- if the same remote path fails again with the same reason and same ETag or
  same content hash, treat it as a known unchanged problem;
- known unchanged problems should update `last_seen_at_utc` / `seen_count`, but
  should not make the sync run fail or block push of unrelated dirty local
  objects;
- if the same remote path fails again with a different ETag or content hash,
  treat it as a fresh problem and surface it as a sync issue;
- if a quarantined object later becomes valid remotely, remove the quarantine
  row and apply it normally.

The settings UI should expose quarantined remote problems separately from the
generic sync status. For each problem, users can:

- clear the local problem row only, leaving the WebDAV file untouched;
- delete the problematic remote file from WebDAV after a strong warning about
  possible data loss. Remote deletion should use the stored ETag as an
  optimistic precondition when available. If the file changed since quarantine,
  refuse deletion and ask the user to run sync again.

## Failure Mode Review

The table below lists the main expected failures and the required safe response.

| Failure | Possible impact | Required response |
| --- | --- | --- |
| Remote unavailable | No objects can be listed/uploaded | Leave local state unchanged, keep dirty objects dirty, show retryable error |
| Wrong credentials | Same as unavailable | Do not clear credentials automatically, show auth error |
| Unsupported manifest | Applying incompatible data could corrupt local DB | Refuse sync before downloading user objects |
| GET fails for one object | Partial pull | Skip that object, keep prior local state, retry next run |
| Clean synced local object is missing remotely | Fresh devices would not restore it | Mark the local object dirty and restore the remote object during push |
| PUT succeeds but app crashes before marking clean | Local object remains dirty | Next run compares content hash/ETag and marks clean or detects conflict |
| Create-only PUT is interrupted mid-upload | Provider may leave partial remote file | Prefer temp PUT + MOVE; invalid final files are quarantined if they still happen |
| Temp upload succeeds but MOVE fails/interruption happens | Orphan temp object | Ignore `.tmp/` during pull; retry the logical dirty object later; cleanup can remove stale temp files |
| Asset upload succeeds but item upload fails | Remote orphan asset | Harmless; item remains dirty and retries later |
| Item arrives before icon asset | Missing icon | Apply item, show default icon, create pending asset ref |
| Secret item arrives before crypto profile | Cannot map to local profile ID | Store deferred encrypted item, retry after profile arrives |
| Reset event arrives with old secret objects | Stale secrets on remote | Reset wins; ignore/purge old generation objects |
| Two devices edit same item | Data conflict | Preserve both sides through conflict copy/state |
| Sync starts while edit dialog is open | User save may overwrite synced change | Block/postpone sync apply through local operation gate |
| Invalid remote JSON | Crash/corruption risk | Quarantine or ignore with reason code |
| Missing parent/cycle | Broken tree | Delay, recover to root/conflict, or quarantine invalid graph |
| Pending refs/deferred rows are left after interrupted apply | UI keeps default icon / hidden deferred data | Retry reconciliation after each pull plan, even when the dependency is already local |
| SQLite apply fails in the middle of a pull plan | Partial local pull state | Roll back the shared SQLite transaction and retry the pull later |
| Remote cleanup beyond temp uploads is not implemented | Remote storage can grow | Accept in v1; add later GC with safe retention rules |

The most important invariant is:

```text
Sync may be incomplete, but it must remain retryable and must not silently lose
user data.
```

## Remote Listing And Performance

V1 can list all objects with `PROPFIND Depth: 1` per directory.

This is acceptable for a bookmark manager with thousands or low tens of
thousands of objects.

Possible later optimizations:

- remote manifest/index files per object category;
- paging/chunked object directories;
- sync journal objects;
- compressed JSON payloads;
- binary icon payload files.

Do not optimize before the correctness model is stable.

## Logging Rules

Allowed logs:

- sync started;
- sync completed;
- sync failed with non-sensitive reason;
- remote repository initialized;
- object counts by category;
- conflict count;
- HTTP status code class;
- retry/backoff state.

Do not log:

- WebDAV full URL;
- username or password;
- bookmark URLs;
- bookmark titles;
- folder names;
- search queries;
- local file paths;
- source hashes;
- object payload JSON;
- encrypted blobs;
- nonces, salts, key material;
- secret generation IDs;
- reset event IDs.

If an object ID is needed for debugging, prefer a short non-reversible local
diagnostic correlation ID generated per sync run, not the persisted object ID.

## UI Requirements

Suggested first UI:

- add a `Sync` section to `SettingsDialog`;
- fields:
  - WebDAV URL;
  - username;
  - password;
  - "Test connection";
  - "Sync now";
- show status through `StatusBanner`;
- show last successful sync time;
- show non-sensitive conflict/error summary;
- never display raw sync object payloads in normal UI.

Initial behavior:

- "Test connection" must be read-only: it may validate credentials and WebDAV
  reachability with a metadata request such as `PROPFIND Depth: 0`, but it must
  not create repository directories, upload manifests, pull objects, or push
  local data;
- manual "Sync now" only;
- no automatic background sync until manual sync is reliable;
- after a successful sync that changed local data, reload the main tree/search.

## Testing Strategy

Test without a real WebDAV server first:

- pure JSON serializer/deserializer tests;
- content-hash canonicalization tests;
- local sync store tests with SQLite temp database;
- fake in-memory WebDAV transport tests;
- conflict decision matrix tests;
- secret reset precedence tests;
- secret item/profile/icon object tests verifying no plaintext leaks into
  secret remote JSON fields.
- deferred secret item tests where the crypto profile arrives later;
- pending icon asset tests where the item is visible with a default icon first;
- invalid remote object quarantine tests;
- local operation gate tests so sync cannot apply over an active editor/DnD
  operation.

Then add optional integration tests against a local WebDAV server only if the
test setup is stable and not required for normal developer runs.

Manual testing should cover:

- first sync from device A to empty remote;
- device B initial pull from remote;
- normal add/edit/delete/move from both devices;
- regular icon upload on A, visible on B;
- secret bookmark + secret icon from A, hidden on B until unlock;
- password change sync;
- master-password reset sync;
- offline conflict scenarios;
- partial/interrupted sync scenarios;
- missing icon asset then later icon asset arrival;
- missing crypto profile then later profile arrival;
- invalid remote object/quarantine;
- sync attempt while an editor dialog is open;
- remote unavailable / wrong credentials / wrong folder.

## Remaining Open Questions

The current v1 direction is implemented as manual sync, JSON objects with
base64 blobs, ETags plus content hashes, conservative conflicts, explicit
quarantine for invalid remote files, and a coarse local operation gate while
editor dialogs/local writes are active.

Remaining sync questions:

- automatic/background sync timing and UI;
- user-facing conflict resolution for normal items;
- richer crypto-profile conflict handling beyond conservative conflict marking;
- remote garbage collection for obsolete/orphaned old sync objects;
- whether remote obsolete secret objects should be deleted automatically after a
  conservative retention period;
- whether the coarse editor/write operation gate should later be replaced with
  optimistic concurrency checks;
- whether a future remote format should split metadata and binary payloads
  instead of storing base64 blobs in one JSON object.

Current recommendation:

- keep manual sync as the only implemented sync trigger until cross-platform
  regression is stable;
- keep conflicts conservative;
- defer aggressive remote cleanup and automatic background sync.
