# Encrypted Secret Icons Architecture

This document records the accepted design for encrypted custom icons on secret
bookmarks.

Important: these decisions are implementation guidance, not an irreversible
contract. If implementation uncovers a simpler or safer design, update this
document before changing direction.

## Goal

Secret bookmarks should support custom icons and discovered favicons without
storing those processed icon images as plaintext SQLite blobs.

The project goal is practical privacy, not maximum cryptographic secrecy. The
main threat is casual inspection of the SQLite file through a database browser,
hex editor, backup copy, or cloud-synced file. It is acceptable for the database
to reveal some metadata if that keeps the feature understandable and avoids
unbounded database growth.

## Current Implementation State

Implemented behavior:

- secret bookmark rows use `items.secret_icon_asset_id` for custom encrypted
  icons;
- `items.icon_asset_id` remains for regular bookmarks and folders;
- converting a normal bookmark to secret can copy/encrypt the regular icon into
  `secret_icon_assets`;
- converting a secret bookmark to normal can decrypt/copy the secret icon into
  `icon_assets`;
- the bookmark editor exposes the same icon choices for secret bookmarks when
  the secret session is unlocked;
- visible secret bookmarks use a decrypted custom icon when available and the
  default bookmark icon otherwise.

## Accepted Design

Use a separate encrypted icon table for secret bookmark icons.

Regular icon table:

```text
icon_assets
```

Secret icon table:

```text
secret_icon_assets
```

Regular bookmarks and folders keep using `items.icon_asset_id`.

Secret bookmarks use a new nullable reference:

```text
items.secret_icon_asset_id
```

Rules:

- non-secret bookmarks and folders use `icon_asset_id`;
- secret bookmarks use `secret_icon_asset_id`;
- a row must not use both `icon_asset_id` and `secret_icon_asset_id`;
- folders do not become secret in the current product model, so folders do not
  use `secret_icon_asset_id`;
- if a secret bookmark has no `secret_icon_asset_id`, show the default bookmark
  icon while the secret bookmark is visible.

When secrets are hidden, secret bookmarks are absent from the UI, so there is no
per-bookmark icon to display.

## Privacy Model

Secret icons must not store processed icon PNG bytes in plaintext.

Accepted metadata leaks:

- `source_hash_algorithm`;
- `source_hash`;
- `source_size_bytes`;
- processed MIME type;
- processed width and height;
- created timestamp;
- fact that two secret icons use the same source hash.

Rationale:

- Deduplication matters for database size.
- Plain SHA-256 source hashes are already used for regular icons.
- This leaks equality and enables dictionary matching for common favicons, but
  that is acceptable for this project because the goal is not maximum secrecy.
- The actual processed icon image should still be encrypted, so opening the
  database does not directly show the favicon/custom image.

Do not store:

- original favicon URL;
- page URL;
- page title;
- local source file path;
- original source bytes;
- decrypted processed icon bytes.

Do not write to logs:

- URLs;
- titles;
- local file paths;
- source hashes;
- encrypted blobs;
- decrypted icon bytes;
- key material, salts, nonces, or secret generation IDs.

## SQLite Shape

Add a table similar to `icon_assets`, but encrypted:

```sql
CREATE TABLE secret_icon_assets (
    id TEXT PRIMARY KEY,

    source_hash_algorithm TEXT NOT NULL,
    source_hash TEXT NOT NULL,
    source_size_bytes INTEGER NOT NULL,

    processed_mime_type TEXT NOT NULL,
    processed_width INTEGER NOT NULL,
    processed_height INTEGER NOT NULL,

    encrypted_processed_bytes BLOB NOT NULL,
    encryption_nonce BLOB NOT NULL,
    payload_format_version INTEGER NOT NULL,

    secret_generation_id TEXT NOT NULL,

    created_at_utc TEXT NOT NULL,

    UNIQUE (source_hash_algorithm, source_hash)
);
```

Add to `items`:

```sql
secret_icon_asset_id TEXT NULL REFERENCES secret_icon_assets(id)
```

Update item constraints so that:

- folders have `secret_icon_asset_id IS NULL`;
- non-secret bookmarks may have `icon_asset_id` but must have
  `secret_icon_asset_id IS NULL`;
- secret bookmarks may have `secret_icon_asset_id` but must have
  `icon_asset_id IS NULL`.

The table stores one encrypted processed PNG per unique source hash inside the
secret icon category. It intentionally does not deduplicate with regular
`icon_assets`.

## Payload Format

The encrypted payload contains only normalized processed icon bytes.

Current normalized representation:

- PNG;
- square;
- `64x64`;
- already processed by the existing icon-processing pipeline before encryption.

The encrypted payload should be versioned with `payload_format_version`.

Recommended v1 plaintext payload before encryption:

```json
{
  "processedBytesBase64": "<base64 PNG bytes>"
}
```

Alternative binary payload is acceptable if the implementation already has a
small binary helper. Keep it simple and versioned.

## Encryption

Use the same runtime DEK as secret bookmark payloads.

Flow:

1. The user unlocks secrets with the master password.
2. The app unwraps the DEK into the runtime secret session.
3. Processed icon PNG bytes are encrypted with AES-GCM using the DEK.
4. Each encrypted icon gets a fresh nonce.
5. The encrypted icon row stores the nonce and encrypted payload.

AAD should bind encrypted icon payloads to their context.

Recommended AAD:

```text
stranichnik:secret-icon:v1:asset:<secret-icon-asset-id>:generation:<secret-generation-id>
```

Use the active profile's `secret_generation_id` at icon creation time.

## Deduplication

Deduplication is required for encrypted secret icons.

Scope:

- deduplicate regular icons only inside `icon_assets`;
- deduplicate secret icons only inside `secret_icon_assets`;
- do not deduplicate across these two tables.

Algorithm:

1. Before processing/encryption, compute SHA-256 over the original downloaded or
   selected source bytes.
2. Check `secret_icon_assets` for
   `(source_hash_algorithm = 'sha256', source_hash = hash)`.
3. If a row exists, reuse it.
4. If no row exists:
   - process source bytes to normalized `64x64` PNG;
   - encrypt processed PNG bytes;
   - insert a new `secret_icon_assets` row.

This reveals same-source equality inside secret icons and allows dictionary
matching for common favicons. This is accepted to keep the database small and
the implementation simple.

## Normal/Secret Conversion Rules

### Normal Bookmark To Secret

If the normal bookmark has `icon_asset_id = NULL`:

- set `secret_icon_asset_id = NULL`;
- use the default bookmark icon while visible.

If the normal bookmark has a regular icon asset:

1. Load the regular icon asset.
2. Check `secret_icon_assets` by the regular asset's source hash.
3. If found, reuse that `secret_icon_asset_id`.
4. If not found, encrypt the regular asset's `processed_bytes` and insert a
   matching secret icon row with the same source hash/source size/processed
   metadata.
5. Store the secret bookmark with `icon_asset_id = NULL` and the chosen
   `secret_icon_asset_id`.

### Secret Bookmark To Normal

If the secret bookmark has `secret_icon_asset_id = NULL`:

- set `icon_asset_id = NULL`;
- use the default bookmark icon.

If the secret bookmark has a secret icon asset:

1. Load the secret icon asset.
2. Check `icon_assets` by the secret asset's source hash.
3. If found, reuse that `icon_asset_id`.
4. If not found:
   - decrypt the secret icon processed PNG bytes;
   - insert a regular `icon_assets` row with the same source hash/source
     size/processed metadata and plaintext `processed_bytes`.
5. Store the normal bookmark with `secret_icon_asset_id = NULL` and the chosen
   `icon_asset_id`.

This intentionally copies between categories instead of sharing one table.

## UI Behavior

Bookmark editor:

- When editing/creating a normal bookmark, keep current icon behavior.
- When editing/creating a secret bookmark and secrets are unlocked, show the same
  icon choices as normal bookmarks:
  - current icon if present;
  - fetched favicon if available;
  - default icon;
  - upload local icon.
- When enabling the secret checkbox on a normal bookmark, preserve the selected
  icon by converting it into a secret icon on save.
- When disabling the secret checkbox on a secret bookmark, preserve the selected
  secret icon by converting it into a regular icon on save.

If secrets are locked and the user tries to create or edit a secret bookmark,
the existing unlock/setup flow should run first. Do not expose secret icon
choices until the secret session is unlocked.

Main tree/search:

- hidden secret bookmarks are not shown, so no icon is shown;
- visible secret bookmarks show:
  - decrypted secret icon if `secret_icon_asset_id` exists;
  - default bookmark icon otherwise.

## Runtime Cache

Decrypting and decoding icon blobs repeatedly can be wasteful. Add a small
in-memory cache for decoded secret icon bitmaps.

Suggested shape:

```text
secret_icon_asset_id -> decoded image
```

The cache must be cleared when:

- secrets are hidden;
- secrets are locked;
- the master password is reset;
- the app exits.

The cache is memory-only. Do not write decrypted icon bytes to disk.

## Reset Master Password

Master-password reset must physically delete encrypted secret icon assets for
the active secret generation.

Because all secret bookmarks are physically purged during reset, their
associated secret icons must be purged too. Delete `secret_icon_assets` rows for
the reset generation in the same storage transaction as:

- inserting `secret_reset_events`;
- deleting secret bookmark rows;
- deleting secret-only folders;
- deleting the active crypto profile.

The reset result count may remain a count of purged secret bookmarks. It does not
need to count purged icon assets unless the UI later needs that number.

## Sync Considerations

Current WebDAV sync treats secret icons as encrypted blobs.

Current sync direction:

- sync regular `icon_assets` separately from `secret_icon_assets`;
- include `source_hash` for deduplication;
- include `secret_generation_id` on secret icon sync objects;
- apply secret reset events to suppress old secret icons from the reset
  generation;
- never upload plaintext secret icon PNG bytes.

Detailed WebDAV sync design now lives in `Notes/WEBDAV_SYNC_ARCHITECTURE.md`.
Keep this data shape in mind when evolving future sync object formats.

## Testing Requirements

Add tests for:

- SQLite migration creates `secret_icon_assets` and `items.secret_icon_asset_id`;
- constraints reject invalid normal/secret icon references;
- creating a secret bookmark with favicon/uploaded icon stores encrypted secret
  icon bytes, not plaintext regular icon bytes;
- identical secret icon source bytes deduplicate inside `secret_icon_assets`;
- identical regular and secret icons can exist separately in both icon tables;
- normal to secret conversion preserves the icon by copying/encrypting it into
  `secret_icon_assets`;
- secret to normal conversion preserves the icon by decrypting/copying it into
  `icon_assets`;
- hidden secret bookmarks do not expose icons in the visible projection;
- visible unlocked secret bookmarks expose decrypted icons;
- hiding/locking secrets clears the decrypted secret icon image cache;
- master-password reset physically deletes secret icon assets for the reset
  generation.

## Implementation Preference

Implement this before WebDAV sync if the user chooses to support secret icons.
That lets the later WebDAV design include the final icon model instead of the
temporary default-icon restriction.
