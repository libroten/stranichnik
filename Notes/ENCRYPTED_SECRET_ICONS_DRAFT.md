# Encrypted Secret Icons Draft

This document is a draft for a future feature: encrypted custom icons for secret bookmarks.

It is not an implementation plan yet. The goal is to preserve the design direction and open questions so the project can return to this topic after the first selective-encryption milestone.

Decisions in this document may change during implementation.

## Current V1 Decision

The first selective-encryption version keeps secret bookmarks without custom icon assets:

- secret bookmark rows use `icon_asset_id = NULL`;
- converting a normal bookmark to secret clears `icon_asset_id`;
- assigning a custom icon to a secret bookmark is rejected;
- UI should show a neutral built-in/default secret/bookmark icon instead of a favicon/custom icon.

This is a temporary privacy-preserving simplification. It avoids leaking bookmark meaning through plaintext favicon/custom icon blobs while the core password, payload encryption, visibility, and search behavior are still being implemented.

## Why Icons Matter For Privacy

For secret bookmarks, an icon can be sensitive metadata:

- a favicon may directly reveal the website;
- a user-provided image may reveal the bookmark topic;
- a source hash of a common favicon can be compared against a precomputed dictionary;
- source image dimensions, file type, URL, local file path, and fetch metadata can also leak information.

Therefore encrypted secret icons should not reuse the current plaintext icon model without additional design.

## Desired Future Behavior

In a future version, secret bookmarks may support icons while secrets are visible:

- default neutral icon is shown while secrets are hidden;
- encrypted custom/favicons are decrypted only after the secret session is unlocked;
- decrypted icon bitmaps exist only in memory while needed;
- decrypted icon cache is cleared when secrets are hidden, locked, or the app exits;
- no URL, title, local source path, plaintext hash, or decrypted icon bytes are written to logs.

When a secret bookmark is converted back to a normal bookmark, the app must make an explicit product decision:

- either discard the encrypted secret icon and keep the normal default icon;
- or decrypt and convert the secret icon into a normal plaintext icon asset after user confirmation.

## Possible Storage Model

Keep regular icons and secret icons separate.

Existing regular icon table:

```text
icon_assets
```

Possible future secret icon table:

```text
secret_icon_assets
```

Possible columns:

```text
id TEXT PRIMARY KEY
crypto_profile_id INTEGER NOT NULL
payload_format_version INTEGER NOT NULL
encrypted_payload BLOB NOT NULL
encryption_nonce BLOB NOT NULL
created_at_utc TEXT NOT NULL
updated_at_utc TEXT NOT NULL
revision INTEGER NOT NULL
sync_state TEXT NOT NULL
modified_device_id TEXT NOT NULL
```

Secret bookmark row then points to a secret icon reference:

```text
secret_icon_asset_id TEXT NULL
```

Alternative: store encrypted icon payload directly in the bookmark row. This is simpler, but worse if multiple bookmarks intentionally share the same icon and less flexible for future sync.

## Icon Payload

The stored encrypted payload should contain only normalized processed icon bytes, not original source metadata.

Recommended normalized representation:

- PNG;
- fixed size, likely the same size as regular processed icons;
- square;
- already resized before encryption.

Do not store inside the encrypted payload unless explicitly needed:

- original favicon URL;
- original local file path;
- original MIME type;
- original dimensions;
- original source hash;
- page title;
- page URL.

## Encryption Approach

Use the same high-level DEK/KEK design as secret bookmarks:

- master password derives KEK;
- KEK unwraps runtime DEK;
- DEK encrypts secret bookmark payloads and future secret icon payloads;
- every secret icon encryption uses a fresh nonce;
- use authenticated encryption;
- bind AAD to item/icon/profile context.

Possible AAD:

```text
stranichnik:secret-icon:v1:icon:<secret-icon-id>:profile:<profile-id>
```

If the icon is stored directly on the bookmark row, bind to bookmark id instead:

```text
stranichnik:secret-bookmark-icon:v1:item:<item-id>:profile:<profile-id>
```

## Deduplication Options

### Option 1: No Deduplication

Store one encrypted icon per secret bookmark.

Pros:

- simplest;
- strongest privacy;
- no plaintext or guessable source hash;
- avoids shared mutable icon semantics.

Cons:

- database can store duplicate icon blobs.

This is probably the best first implementation because processed icons are small.

### Option 2: Deduplicate While Unlocked With Keyed Hash

After processing an icon, compute an HMAC over normalized icon bytes:

```text
HMAC-SHA256(secretIconHashKey, normalizedIconBytes)
```

Store only the HMAC, not a plaintext hash.

Pros:

- can deduplicate identical secret icons;
- harder to dictionary-match without the key.

Cons:

- needs a derived secret hash key;
- complicates password change and sync;
- still reveals equality between secret icons inside the same database/profile.

Open question: derive `secretIconHashKey` from DEK with HKDF-like key separation, or store a separate wrapped key?

### Option 3: Plain Source Hash

Reuse current icon deduplication based on source hash.

Pros:

- simple;
- matches existing regular icon design.

Cons:

- weak privacy;
- common favicons can be dictionary-matched.

This option is not recommended for secret icons.

## UI Ideas

When editing a secret bookmark while secrets are visible:

- show current secret icon if it exists;
- show neutral default secret/bookmark icon;
- allow loading a local image;
- optionally allow using fetched favicon from metadata fetch;
- clearly avoid showing/storing the favicon option if the user cancels secret unlock.

When secrets are hidden:

- do not show per-bookmark secret icons;
- use only neutral default icon;
- clear decrypted icon cache.

## Sync Considerations

Secret icons should sync as encrypted blobs.

Open decisions:

- whether encrypted icon assets have independent tombstones;
- whether icon changes are merged separately from bookmark changes;
- whether deduplication identifiers are stable across devices;
- how to handle a device that has encrypted icon blobs but has not unlocked secrets yet.

## Open Questions Before Implementation

1. Should v1 encrypted icons use no deduplication?
2. Should secret icons live in a separate table or directly on bookmark rows?
3. Should secret icon IDs be independent entities or owned by bookmark rows?
4. Should converting secret bookmark to normal discard the encrypted icon or offer to convert it to a normal icon?
5. Should encrypted favicons be fetched automatically, or only after explicit user action?
6. Should secret icon cache clear on hide only, or also after an inactivity timeout even if the runtime key remains unlocked?
7. Should encrypted icons be searchable/indexed in any way? Current answer should likely be no.
8. Should sync support secret icon deduplication across devices, or accept duplicate encrypted blobs?

## Recommendation For First Future Implementation

Use the simplest private model:

- no deduplication for secret icons;
- separate `secret_icon_assets` table;
- store normalized encrypted PNG bytes only;
- use DEK encryption with icon-specific AAD;
- clear decrypted icon cache whenever secrets are hidden;
- keep regular `icon_assets` unchanged for non-secret bookmarks.

After this works, consider keyed-HMAC deduplication only if database size becomes a real problem.
