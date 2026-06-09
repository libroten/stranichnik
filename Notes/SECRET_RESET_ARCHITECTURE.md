# Secret Master Password Reset Architecture

This note records the accepted design for resetting the secret-bookmark master password.

Important: this is still a working architecture. It may change when WebDAV sync is implemented, but future changes should preserve the core privacy and storage-size goals described here.

## Problem

If the user forgets the master password, Stranichnik cannot decrypt existing secret bookmarks.

The app needs a destructive reset operation:

- remove the old master password;
- remove all secret bookmarks that were encrypted with the old secret data key;
- allow the user to configure a new master password later;
- avoid keeping old encrypted payload blobs forever;
- remain compatible with future WebDAV item-level sync.

The naive approach is to tombstone every secret bookmark row in `items` and keep the encrypted payloads in those tombstones. That is not acceptable for master-password reset because it can make the SQLite database grow without bound.

## Accepted Direction

Separate live application state from compact sync ledger data.

For master-password reset:

1. Create one compact `secret_reset_event`.
2. Physically purge secret bookmark rows and their encrypted payloads from the live `items` table.
3. Physically purge encrypted secret icon assets for the reset generation.
4. Physically purge folders that only contained secret bookmark content and would otherwise become newly visible empty folders.
5. Delete the active crypto profile for the old secret generation.
6. Lock the runtime secret session and forget the in-memory data key.

This keeps the sync intent without keeping the heavy encrypted bookmark payloads.

The important rule is:

```text
Keep deletion intent, not deleted payload.
```

## Secret Generations

`crypto_profiles.id` is not enough to identify a secret generation.

The current app uses one active profile id. If reset deletes the profile and a later setup creates another active profile with the same id, old and new secrets would be hard to distinguish in future sync code.

Therefore each crypto profile has a separate `secret_generation_id`.

Meaning:

- password change preserves the same `secret_generation_id`;
- normal secret bookmark edits preserve the same generation;
- master-password reset creates a reset event for the current generation and deletes that profile;
- the next first-time password setup creates a new profile with a new `secret_generation_id`.

Future WebDAV sync can then interpret a reset event as:

```text
All secret items from generation X are obsolete and must not be restored.
```

## SQLite Shape

The active crypto profile stores:

```text
crypto_profiles.secret_generation_id TEXT NOT NULL
```

Master reset events are stored separately:

```text
secret_reset_events
  id TEXT PRIMARY KEY
  secret_generation_id TEXT NOT NULL UNIQUE
  reset_at_utc TEXT NOT NULL
  reset_device_id TEXT NOT NULL
  sync_state TEXT NOT NULL
  remote_etag TEXT NULL
  last_synced_at_utc TEXT NULL
```

The reset event intentionally does not store:

- bookmark title;
- bookmark URL;
- encrypted bookmark payload;
- local file paths;
- icon blobs;
- raw hashes;
- master password data;
- DEK/KEK material.

## Reset Algorithm

The reset operation should be a single storage transaction where possible.

SQLite reset transaction:

1. Confirm an active crypto profile exists.
2. Insert a `secret_reset_events` row for the active `secret_generation_id`.
3. Physically delete all bookmark rows from `items` where:
   - `item_type = 'bookmark'`;
   - `is_secret = 1`.
4. Physically delete folders that:
   - have at least one secret bookmark descendant;
   - have no visible non-secret content when secrets are hidden.
5. Physically delete `secret_icon_assets` rows for the active `secret_generation_id`.
6. Delete the active row from `crypto_profiles`.
7. Commit.

After commit:

1. `SecretSessionService.MarkNotConfigured()` disposes the runtime data key.
2. Main view model reloads the visible tree and in-memory search index.
3. Secret bookmarks are gone from UI and search.
4. A new master password can be set later as a fresh generation.

## Why Physical Delete Is Acceptable Here

Normal item deletion still needs tombstones so future sync can propagate individual deletes.

Master-password reset is different:

- it is a bulk destructive operation;
- the user explicitly asks to discard all secret data;
- keeping every old encrypted payload defeats the purpose of controlling database growth;
- future sync can use one reset event to suppress old secret items from the same generation.

Therefore master reset uses physical delete for secret bookmark rows and compact reset events for sync intent.

## Future WebDAV Sync Behavior

When a device uploads a reset event:

- remote sync stores the reset event as a small versioned object;
- old remote secret item objects for that generation should be ignored;
- a later cleanup pass may delete those old remote item objects.

When another device downloads a reset event:

1. If it has secret bookmarks belonging to the reset generation, physically purge them locally.
2. Physically purge local secret icon assets belonging to the reset generation.
3. If its active crypto profile belongs to the reset generation, delete that profile and lock the session.
4. Mark the reset event as applied/synced according to the future sync protocol.
5. Do not resurrect old secret bookmarks or old secret icon assets from remote objects that belong to the reset generation.

Open sync details:

- exact remote object format;
- how devices acknowledge reset events;
- when it is safe to garbage-collect old remote reset events;
- whether remote secret item objects are deleted immediately or only after a conservative retention period.

## SQLite File Size

SQLite `DELETE` removes rows logically and frees pages for reuse inside the database.

It does not always shrink the database file on disk immediately.

To physically compact the file, the app may later add:

- a manual "compact database" maintenance action;
- an explicit `VACUUM` after destructive reset;
- or an auto-vacuum strategy chosen before database creation.

Do not silently run expensive compaction without considering UI responsiveness and disk-space requirements. `VACUUM` rewrites the database and can be slow for large databases.

The current reset design prevents unbounded retained payload growth. Physical file shrinking is a separate database maintenance concern.

## Logging Rules

Allowed reset logs:

- reset started;
- reset completed;
- reset failed with a non-sensitive reason;
- schema migration applied.

Do not log:

- bookmark titles;
- bookmark URLs;
- folder names;
- master passwords;
- key material;
- salts/nonces/ciphertexts;
- `secret_generation_id`;
- reset event ids.

## UI Requirements

The reset action lives in Settings under the secret-bookmark section.

It must:

- be visually dangerous/destructive;
- explain that all secret bookmarks will be permanently deleted;
- explain that this cannot recover forgotten data;
- require an additional explicit confirmation;
- work even when the secret session is locked, because forgotten-password reset must not require the old password.

On success:

- show a success status banner;
- clear any password fields;
- leave the app in a not-configured secret state.

On failure:

- show a status banner with a non-sensitive error.
