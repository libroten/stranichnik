# Stranichnik Selective Encryption Architecture

This note designs selective encryption for secret bookmarks.

Important: this is a working architecture, not an irreversible contract. If implementation reveals a safer, simpler, or more maintainable approach, update this document and the implementation plan before continuing.

## Primary Goal

Stranichnik should allow individual bookmarks to be marked as secret.

When secret bookmarks are hidden:

- their title and URL must not be visible in the UI;
- they must not appear in search results;
- folders that contain only hidden secret bookmarks must also be hidden from the visible tree;
- their title and URL must not be readable as plaintext from the SQLite file;
- opening the SQLite database and running SQL queries must not reveal their title or URL.

The project goal is practical local privacy from casual inspection. This is not intended to be a hardened password manager or a high-assurance cryptographic product.

## User Requirements

UI behavior:

- Secret bookmarks are hidden after application startup.
- Secret bookmarks can be shown or hidden with a keyboard shortcut:
  - macOS: `Cmd+P`;
  - Windows/Linux: `Ctrl+P`.
- After one minute without UI activity, visible secret bookmarks are automatically hidden.
- "No UI activity" means no meaningful pointer, keyboard, text, scroll, menu, dialog, or command interaction in the app.
- During one application run, the master password should be required only once.
- After the password has been entered once during the current run, later hide/show operations should not ask for it again.
- The application must handle the first-use case where no master password exists yet.
- The application must support changing the master password.

Storage behavior:

- Secret bookmark plaintext must not be stored in SQLite.
- Secret bookmark plaintext search data must not be stored on disk.
- The SQLite file may reveal that a secret row exists. Hiding the existence of secret bookmarks is not a goal for the first version.

## Threat Model

The first version protects against:

- a curious person opening `stranichnik.sqlite` in a SQLite viewer;
- a curious person searching the SQLite file in a hex editor;
- a backup/cloud copy of the SQLite file being inspected without the master password;
- accidental plaintext leakage through logs, search indexes, or metadata tables.

The first version does not fully protect against:

- malware running as the same OS user while Stranichnik is unlocked;
- memory inspection of the running process;
- keyloggers;
- screen capture while secrets are visible;
- OS swap/pagefile exposure;
- a weak or reused master password being guessed offline;
- someone inferring a secret from non-encrypted metadata such as parent folder name, timestamps, row count, item IDs, or optional icon metadata.

## Security Non-Goals

The first implementation does not try to:

- encrypt the entire SQLite database;
- hide the existence or count of secret bookmarks;
- hide folder names;
- encrypt custom icon assets in the first implementation;
- protect against a compromised machine;
- implement searchable encryption;
- implement a full password-manager security model.

## Sources And Rationale

The design follows these current primary references:

- OWASP Cryptographic Storage Cheat Sheet:
  - prefer AES with a secure mode;
  - avoid custom algorithms;
  - prefer authenticated modes such as GCM or CCM;
  - a password-derived Key Encryption Key can wrap a random Data Encryption Key.
  - https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html
- OWASP Password Storage Cheat Sheet:
  - password-based derivation needs salts and a configurable work factor;
  - Argon2id is preferred where available;
  - PBKDF2-HMAC-SHA256 with a high iteration count remains a supported practical option.
  - https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
- Microsoft .NET crypto APIs:
  - `AesGcm` for AES-GCM authenticated encryption;
  - `Rfc2898DeriveBytes.Pbkdf2` for PBKDF2 key derivation;
  - `RandomNumberGenerator` for cryptographically strong random bytes;
  - `CryptographicOperations.ZeroMemory` and `FixedTimeEquals` for sensitive-memory and comparison helpers.
  - https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm
  - https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes
  - https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes
  - https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations

## High-Level Design

Use application-level selective encryption.

Do not encrypt the entire SQLite database in the first version.

Use a DEK/KEK structure:

- DEK: random Data Encryption Key, used to encrypt secret bookmark payloads.
- KEK: Key Encryption Key derived from the user's master password, used only to wrap/unwrap the DEK.

Why use DEK/KEK instead of deriving the bookmark encryption key directly from the password:

- changing the master password only re-wraps the DEK;
- secret bookmark rows do not need to be rewritten on password change;
- future WebDAV sync does not need to upload every secret bookmark after password change;
- the design follows common key-management practice while still being understandable.

Tradeoff:

- there is more metadata and code than in the direct password-derived-key approach;
- runtime unlock state must hold the DEK in memory until the user locks or exits.

This tradeoff is acceptable because password changes and future sync matter to this project.

## Terminology

`Secret configured`:

- A crypto profile exists in SQLite.
- The app has a wrapped DEK and can verify a master password.

`Session unlocked`:

- The user has entered the master password successfully during the current app run.
- The app has unwrapped the DEK and keeps it in runtime memory.
- This state lasts until app exit or explicit session lock.

`Secrets visible`:

- Secret bookmarks are currently shown in the tree and search.
- This can be toggled without asking for the password again if the session is already unlocked.

`Secrets hidden`:

- Secret bookmarks are not shown in the tree or search.
- The session may still be unlocked in memory.
- Auto-hide after inactivity changes visibility, not the unlocked runtime key state.

This distinction matters:

- "hide" should be fast and reversible without password during the same run;
- "lock session" should forget the runtime DEK and require the password again.

## Runtime State Machine

Recommended states:

```text
NotConfigured
ConfiguredLocked
ConfiguredUnlockedHidden
ConfiguredUnlockedVisible
```

`NotConfigured`:

- no master password exists;
- no secret bookmarks should exist;
- pressing the reveal shortcut should show a friendly message or no-op;
- marking a bookmark as secret should first start password setup.

`ConfiguredLocked`:

- a crypto profile exists;
- no runtime DEK is loaded;
- secret bookmarks are hidden;
- pressing reveal asks for the master password.

`ConfiguredUnlockedHidden`:

- runtime DEK is loaded;
- secret bookmarks are hidden;
- pressing reveal shows secrets without password prompt.

`ConfiguredUnlockedVisible`:

- runtime DEK is loaded;
- secret bookmarks are visible and searchable;
- pressing shortcut hides secrets without forgetting the runtime DEK;
- inactivity timeout returns to `ConfiguredUnlockedHidden`.

Optional later state:

```text
ConfiguredUnlockedChangingPassword
```

This can be represented by dialog state rather than a global state.

## Cryptographic Algorithms

### First-Version Choice

Use:

- KDF: PBKDF2-HMAC-SHA256 via `Rfc2898DeriveBytes.Pbkdf2`.
- PBKDF2 salt: 32 random bytes.
- PBKDF2 iterations: store per crypto profile; initial target `600000`.
- KEK length: 32 bytes.
- DEK length: 32 bytes.
- Payload encryption: AES-256-GCM via `AesGcm`.
- AES-GCM nonce: 12 random bytes per encryption.
- AES-GCM tag: 16 bytes.
- Randomness: `RandomNumberGenerator`.

Why not Argon2id first:

- Argon2id is a good password KDF, but .NET does not provide it as a built-in API.
- Adding an Argon2id package may be worthwhile later, but it adds dependency choice, licensing, cross-platform native/managed concerns, and maintenance.
- PBKDF2-HMAC-SHA256 is built into .NET, simple, testable, and acceptable for this project's first practical privacy goal.

Future upgrade:

- Add an Argon2id-based crypto profile version later.
- Existing crypto profiles should remain decryptable because each profile stores its KDF metadata.

### Password KDF Cost

Initial PBKDF2 iterations:

```text
600000
```

This is intentionally conservative and matches OWASP's current PBKDF2-HMAC-SHA256 password-storage recommendation.

Implementation note:

- Store iteration count in SQLite.
- Do not hardcode a single global constant as the only source of truth.
- If profiling on target machines shows unacceptable delay, discuss changing the default rather than silently lowering it.
- Do not change old profiles automatically without a planned migration.

### AES-GCM AAD

Use Additional Authenticated Data to bind ciphertext to context.

For wrapped DEK:

```text
stranichnik:wrapped-dek:v1:profile:<profile-id>
```

For password-check payload:

```text
stranichnik:password-check:v1:profile:<profile-id>
```

For bookmark payload:

```text
stranichnik:bookmark-secret:v1:item:<item-id>:profile:<profile-id>
```

Why:

- AAD is not encrypted, but it is authenticated.
- It prevents accidentally accepting a ciphertext in the wrong context.
- It binds secret payloads to item/profile identity.

Do not include user title or URL in AAD because AAD is plaintext metadata.

## SQLite Schema

SQLite persistence for secret bookmarks is implemented.

The current schema version rebuilds the earlier draft `crypto_profiles` and `items`
tables into the shape below.

### Current `crypto_profiles`

```sql
CREATE TABLE crypto_profiles (
    id INTEGER PRIMARY KEY,

    profile_version INTEGER NOT NULL,

    kdf_name TEXT NOT NULL,
    kdf_hash_algorithm TEXT NOT NULL,
    kdf_iterations INTEGER NOT NULL,
    kdf_salt BLOB NOT NULL,

    kek_length_bytes INTEGER NOT NULL,
    data_key_algorithm TEXT NOT NULL,
    wrapped_data_key BLOB NOT NULL,
    wrapped_data_key_nonce BLOB NOT NULL,

    encryption_algorithm TEXT NOT NULL,
    payload_format TEXT NOT NULL,

    password_check_payload BLOB NOT NULL,
    password_check_nonce BLOB NOT NULL,

    secret_generation_id TEXT NOT NULL,

    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
```

Initial values:

```text
profile_version: 1
kdf_name: PBKDF2
kdf_hash_algorithm: SHA256
kdf_iterations: 600000
kek_length_bytes: 32
data_key_algorithm: AES-256-GCM-DEK
encryption_algorithm: AES-256-GCM
payload_format: stranichnik-secret-json-v1
secret_generation_id: random opaque ID, generated once per secret-data generation
```

### Current `items` Secret Fields

Secret-related fields:

```sql
is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1)),
encrypted_payload BLOB NULL,
encryption_nonce BLOB NULL,
crypto_profile_id INTEGER NULL REFERENCES crypto_profiles(id),
secret_payload_format_version INTEGER NULL
```

For normal bookmarks:

```text
is_secret = 0
title IS NOT NULL
url IS NOT NULL
encrypted_payload IS NULL
encryption_nonce IS NULL
crypto_profile_id IS NULL
secret_payload_format_version IS NULL
```

For secret bookmarks:

```text
is_secret = 1
title IS NULL
url IS NULL
encrypted_payload IS NOT NULL
encryption_nonce IS NOT NULL
crypto_profile_id IS NOT NULL
secret_payload_format_version = 1
```

### Icon Policy For Secret Bookmarks

Current implementation:

- Secret bookmarks must have `icon_asset_id = NULL`.
- Secret bookmarks may have `secret_icon_asset_id`.
- Secret icon assets are stored in `secret_icon_assets`.
- Processed secret icon PNG bytes are encrypted with the runtime secret DEK.
- Secret icon source hashes are stored in plaintext for deduplication inside the secret icon category.
- When converting a normal bookmark to secret, an existing regular icon is copied/encrypted into `secret_icon_assets` when possible.
- When converting a secret bookmark back to normal, an existing secret icon is decrypted/copied into `icon_assets` when possible.

See `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md` for details.

### Secret Folders

First version:

- Do not support secret folders.
- Only bookmarks can be secret.

Reason:

- The user's requirement is secret bookmarks.
- Secret folders would require deciding whether folder names, child structure, and descendant visibility are encrypted or hidden.
- This can be designed later if needed.

## Secret Payload Format

Secret bookmark plaintext DTO:

```csharp
public sealed record SecretBookmarkPayloadV1(
    string Title,
    string Url);
```

Future fields can be added with a new version:

- notes;
- tags;
- cached metadata;
- search projection text.

Encrypted payload envelope:

```csharp
public sealed record EncryptedPayloadEnvelopeV1(
    int Version,
    string Algorithm,
    string Tag,
    string Ciphertext);
```

Storage:

- Serialize `EncryptedPayloadEnvelopeV1` as compact UTF-8 JSON.
- Store those bytes in `items.encrypted_payload`.
- Store the AES-GCM nonce in `items.encryption_nonce`.
- Store profile ID in `items.crypto_profile_id`.

Why JSON instead of a custom binary format first:

- easy to version;
- easy to test;
- easy to inspect structurally without exposing plaintext;
- payloads are tiny, so overhead is acceptable.

Do not log serialized envelopes.

## Master Password Setup

First-use scenario:

1. User opens bookmark editor.
2. User enables "Secret" on a bookmark.
3. App checks whether a crypto profile exists.
4. If no profile exists, app opens `SetMasterPasswordDialog`.
5. Dialog asks for:
   - master password;
   - repeat master password.
6. Dialog validates:
   - not empty;
   - repeat matches;
   - optional minimum length/passphrase guidance.
7. If user cancels:
   - the "Secret" toggle returns to off;
   - editor stays open;
   - no profile is created.
8. If user confirms:
   - app creates crypto profile;
   - app generates random DEK;
   - app derives KEK from password;
   - app wraps DEK with KEK;
   - app creates password-check payload;
   - session becomes `ConfiguredUnlockedHidden` or `ConfiguredUnlockedVisible` depending on context;
   - bookmark can now be saved as secret.

Recommended UX:

- Creating the first secret bookmark should leave secrets visible after save, because the user just opted into secret work.
- If this feels surprising during UI testing, discuss changing it.

## Master Password Unlock

Shortcut behavior when configured but locked:

1. User presses `Cmd+P` on macOS or `Ctrl+P` on Windows/Linux.
2. App opens `UnlockSecretsDialog`.
3. User enters master password.
4. App derives KEK from the stored profile metadata.
5. App tries to decrypt the password-check payload and unwrap the DEK.
6. If authentication fails:
   - show a friendly error;
   - keep secrets hidden;
   - do not reveal whether a specific part failed.
7. If authentication succeeds:
   - store DEK in runtime session;
   - show secret bookmarks;
   - rebuild visible tree/search to include decrypted secret bookmarks;
   - start/reset inactivity timer.

Do not log failed password values or derived-key details.

Safe logs:

- `Secret unlock failed.`
- `Secret unlock succeeded.`
- `Secrets shown.`
- `Secrets hidden by user action.`
- `Secrets hidden by inactivity timeout.`

Do not log counts if the count itself is considered sensitive. If counts are logged, keep them coarse and confirm with the user first. First version should avoid counts.

## Show/Hide Shortcut

Shortcut:

```text
macOS: Cmd+P
Windows/Linux: Ctrl+P
```

Implementation direction:

- Handle in `MainWindow.axaml.cs` from `OnKeyDown`.
- Use Avalonia key modifiers.
- Platform detection can use runtime OS checks.
- Do not conflict with text input in dialogs:
  - if a modal dialog is open, let the dialog handle keys;
  - global shortcut should be active in the main window.

Behavior:

```text
NotConfigured:
    Show "No master password has been set yet" or no-op.

ConfiguredLocked:
    Prompt for password.
    On success: show secrets.

ConfiguredUnlockedHidden:
    Show secrets immediately.

ConfiguredUnlockedVisible:
    Hide secrets immediately.
```

Important:

- Hiding secrets via shortcut does not clear the runtime DEK.
- The user should not need to re-enter the password after hiding/revealing during the same app run.

## Inactivity Auto-Hide

Requirement:

- After one minute without UI activity, visible secret bookmarks automatically hide.

Recommended implementation:

- Add `SecretInactivityService` or keep a dedicated timer in the main window for the first slice.
- Use a one-minute `DispatcherTimer`.
- Reset the timer on meaningful app input:
  - pointer pressed;
  - pointer released;
  - pointer wheel;
  - key down;
  - text input;
  - menu action;
  - dialog action;
  - drag/drop events.
- If secrets are not visible, the timer can stay stopped.
- If secrets become visible, start/reset the timer.
- On timeout:
  - hide secrets;
  - clear search query or rebuild search without secret entries;
  - keep runtime DEK in memory;
  - log a generic auto-hide event.

Open design choice:

- Pointer movement alone can be noisy. The user said "no clicks and no interaction"; pointer movement over the app may or may not count as interaction.
- First recommendation: count pointer movement only during drag/pan or when a button is pressed, not passive mouse hover.
- If the user expects passive mouse movement to reset timeout, this can be changed later.

## Search Integration

When secrets are hidden or session is locked:

- search index contains only non-secret bookmarks;
- secret bookmarks are absent from search results;
- existing query should be recalculated against visible data only.

When secrets become visible:

- decrypt secret bookmark payloads;
- add decrypted secret bookmarks to the in-memory search index;
- refresh current query results.

When secrets are hidden:

- remove secret bookmarks from the in-memory search index;
- refresh current query results.

Implementation preference:

- Rebuild the full search index from the current visible projection after visibility changes.
- This is simpler and safer than trying to incrementally remove only secret IDs in the first implementation.

Privacy rule:

- Never persist plaintext secret search terms to disk.

## Tree Projection

Recommended layering:

```text
SqliteBookmarkTreeStore.Load()
    -> raw BookmarkTreeSnapshot with encrypted secret records
SecretBookmarkProjectionService
    -> visible/decrypted BookmarkTreeSnapshot
BookmarkTreeViewModelMapper
    -> Avalonia view models
BookmarkSearchService
    -> in-memory visible search index
```

`SqliteBookmarkTreeStore.Load()` may return secret records with:

- `IsSecret = true`;
- `Title = null`;
- `Url = null`;
- `EncryptedPayload != null`.

Folder visibility rules while secrets are hidden:

- If a folder contains only hidden secret bookmarks, the folder itself is hidden.
- If a folder contains only subfolders that become hidden by the same rule, the parent folder is also hidden.
- If a folder contains at least one visible bookmark or at least one visible folder, the folder remains visible.
- Genuinely empty user-created folders remain visible. Only folders whose visible emptiness is caused by hidden secret descendants should disappear.
- Since folders are not secret in v1, folder title encryption is not involved.

The mapper should not see encrypted records directly unless they have been decrypted or filtered first.

Why:

- View models should not become responsible for decryption.
- Search should consume the same visible/decrypted projection as the tree.
- This keeps encryption outside Avalonia-specific code.

## Application Service Direction

Encryption will add enough orchestration that `MainWindowViewModel` may become too large.

Recommended direction:

- Introduce `BookmarkTreeApplicationService` during the encryption work if it reduces complexity.
- It can coordinate:
  - store operations;
  - secret session state;
  - encryption/decryption;
  - visible projection rebuild;
  - search index rebuild/update;
  - operation results for UI.

Do not over-abstract too early. If a small projection service and session service are enough for the first slice, keep it smaller.

## CRUD Behavior

### Add Normal Bookmark

- Same as today.
- Store plaintext title and URL.
- Search index updates immediately if search is active.

### Add Secret Bookmark

Preconditions:

- crypto profile exists or is created during editor flow;
- session is unlocked;
- title and URL are available in memory from the editor.

Steps:

1. Build `SecretBookmarkPayloadV1`.
2. Encrypt with DEK.
3. Insert item:
   - `item_type = bookmark`;
   - `is_secret = 1`;
   - `title = NULL`;
   - `url = NULL`;
   - `icon_asset_id = NULL`;
   - set `secret_icon_asset_id` when the editor selected a secret icon;
   - encrypted payload fields set.
4. Update visible tree:
   - if secrets visible: add decrypted bookmark view model;
   - if secrets hidden: do not show it.
5. Update search:
   - if secrets visible: include it;
   - if secrets hidden: do not include it.

### Edit Normal Bookmark To Secret

Steps:

1. Ensure crypto profile and unlocked session.
2. Build encrypted payload from current editor title and URL.
3. Update row:
   - set `is_secret = 1`;
   - clear plaintext `title` and `url`;
   - clear `icon_asset_id`;
   - convert the selected or existing regular icon to `secret_icon_asset_id` when possible;
   - set encrypted payload fields.
4. Update tree/search according to current visibility.

### Edit Secret Bookmark While Visible

Steps:

1. Editor receives decrypted title and URL.
2. Saving re-encrypts the new payload with a new random nonce.
3. Row remains secret.
4. Refresh tree/search.

### Edit Secret Bookmark To Normal

Steps:

1. Decrypt existing payload.
2. Save plaintext title and URL.
3. Clear secret fields.
4. Convert `secret_icon_asset_id` back into a regular `icon_asset_id` when possible.

### Delete Secret Bookmark

- Same tombstone behavior as normal bookmarks.
- Do not clear encrypted payload immediately if tombstones need sync.
- Future sync can propagate encrypted tombstone metadata.
- If permanent cleanup is implemented later, encrypted payload may be removed then.

This section describes ordinary delete for individual items. Master-password reset is a separate destructive bulk operation and intentionally does not keep full encrypted payload tombstones. Reset design is documented in `Notes/SECRET_RESET_ARCHITECTURE.md`.

### Move Secret Bookmark

- Moving does not need decryption.
- Moving updates parent/sort metadata only.
- If hidden, user cannot directly move a hidden secret bookmark from the UI.
- If moving a folder that contains hidden secret bookmarks, descendants move naturally because parent relationships remain in storage.

## Master Password Change

User-facing scenario:

1. User opens a future security/settings action.
2. If no master password exists:
   - show set-password dialog instead.
3. If session is locked:
   - ask for current password first.
4. If session is already unlocked:
   - do not require current password again during the same app run, unless the user explicitly wants stricter behavior later.
5. Ask for new password and confirmation.
6. Derive new KEK with new salt and KDF metadata.
7. Re-wrap existing DEK with new KEK.
8. Create new password-check payload.
9. Update `crypto_profiles`.
10. Do not rewrite each secret bookmark.

Important:

- Password change should be transactional.
- If the app crashes during password change, either the old profile or new profile must remain usable.
- First implementation can keep one active crypto profile and update it in a SQLite transaction.
- More advanced future implementation can create a new profile row and then switch active profile metadata.

Open design choice:

- Current `crypto_profiles.id` is referenced by secret items.
- If password change only re-wraps DEK in the same profile row, item references remain stable.
- If creating a new profile row, all secret items would need `crypto_profile_id` updated or the old profile kept.
- First recommendation: update the same profile row transactionally.

## Master Password Loss

There is no recovery if the user forgets the master password.

The app should communicate this clearly when setting the password.

Accepted reset direction:

- "Forget encrypted data and reset master password."
- Create one compact secret reset event for the active secret generation.
- Physically purge secret bookmark rows, encrypted payloads, and folders that only contained secret bookmark content from live storage.
- Delete the old active crypto profile.
- Do not keep full encrypted payload tombstones for every secret bookmark.
- Future WebDAV sync should treat the reset event as authoritative and must not resurrect old secret items from that generation.

Details are in `Notes/SECRET_RESET_ARCHITECTURE.md`.

## UI Components To Add

Likely new dialogs:

- `SetMasterPasswordDialog`
- `UnlockSecretsDialog`
- `SettingsDialog`
  - first section: secret bookmarks;
  - contains the UI for setting or changing the master password;
  - designed as a scrollable settings window so future sections can be added below.

Possible future security menu:

- `Service -> Security`
- Or `Stranichnik -> Settings` when real settings are implemented.

First slice may avoid menu work and only implement:

- password setup from bookmark editor when enabling secret;
- unlock prompt from shortcut;
- later add change-password UI.

Editor changes:

- Add a "Secret bookmark" toggle/checkbox to bookmark editor only.
- Do not show this toggle for folders.
- If enabled and no master password exists, run first-password setup.
- If enabled and session locked, ask for unlock.
- If canceled, revert toggle to off.
- Secret bookmarks can use encrypted custom/favicons as described in `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md`.

## Keyboard Shortcut Details

Shortcut detection:

- macOS: `KeyModifiers.Meta` + `Key.P`.
- Windows/Linux: `KeyModifiers.Control` + `Key.P`.

Implementation note:

- Avalonia's modifier naming should be verified in the current project during implementation.
- Avoid triggering when focus is inside password dialogs.
- Mark the event handled only when the shortcut is accepted.

## Inactivity Tracking Details

Recommended first implementation:

- `SecretActivityMonitor` or a small service owned by `MainWindow`.
- Public method: `NotifyActivity()`.
- Public method: `StartVisibleSecretsTimer()`.
- Public method: `StopVisibleSecretsTimer()`.
- Event/callback: `InactivityTimeout`.

Activity sources in `MainWindow`:

- `OnKeyDown`
- `PointerPressed`
- `PointerReleased`
- `PointerWheelChanged`
- menu click handlers
- context menu click handlers
- dialog completion callbacks
- drag start/update/drop
- search text changes can notify through view model or text box event.

Do not reset timer from passive hover unless the user asks for that behavior.

## Logging Policy

Allowed logs:

- crypto profile created;
- master password setup canceled;
- unlock succeeded;
- unlock failed;
- secrets shown;
- secrets hidden by shortcut;
- secrets hidden by inactivity;
- password change started/completed/failed;
- secret projection rebuild started/completed;
- decryption failed for a row, without item title/URL.

Forbidden logs:

- master password;
- derived keys;
- DEK or KEK;
- salts;
- nonces;
- ciphertext;
- encrypted payload JSON;
- bookmark URLs;
- bookmark titles;
- folder titles;
- raw hashes;
- local file paths for user-selected data.

Even item IDs should be avoided in routine logs unless needed for a specific diagnostic, because they are persistent user-data identifiers.

## Failure Handling

Wrong password:

- show a friendly localized error;
- do not reveal whether the password check or DEK unwrap failed.

Corrupt encrypted payload:

- do not crash the app;
- keep the problematic secret bookmark hidden or show a non-sensitive placeholder only in an unlocked diagnostics path;
- log a generic decryption failure without item content.

Unsupported crypto profile:

- do not attempt decryption;
- show a localized "unsupported encryption format" message if the user tries to unlock;
- keep data unchanged.

AES-GCM not supported:

- `AesGcm.IsSupported` should be checked at startup or crypto-service construction.
- If unsupported on a target platform, show a clear message and disable secret bookmark creation.
- For macOS/Windows/Linux on supported .NET desktop runtimes this is expected to work, but code should still be defensive.

## Localization

All new UI strings must go through:

- `Resources/Strings.resx`;
- `Resources/Strings.ru.resx`;
- `Localization/UiStrings.cs`.

Likely strings:

- Secret bookmark
- Set master password
- Unlock secret bookmarks
- Change master password
- Current password
- New password
- Repeat password
- Passwords do not match
- Could not unlock secret bookmarks
- Secret bookmarks are hidden
- Secret bookmarks are visible
- Secret bookmarks were hidden due to inactivity
- There is no password recovery

Do not hardcode these strings in XAML/code-behind.

## Testing Strategy

Crypto tests:

- password setup creates a valid profile;
- correct password unwraps DEK;
- wrong password fails;
- AES-GCM round-trip works;
- same plaintext encrypted twice produces different ciphertext/nonces;
- corrupted ciphertext/tag fails;
- corrupted AAD fails;
- changing password keeps existing secret payload decryptable;
- changing password makes old password fail and new password succeed.

Storage tests:

- secret bookmark rows do not store plaintext title/url;
- normal bookmark rows still store plaintext title/url;
- secret bookmark insert rejects missing encrypted payload;
- normal bookmark insert rejects encrypted payload fields;
- changing normal to secret clears title/url/icon;
- changing secret to normal restores title/url and clears encrypted fields;
- tombstone delete preserves encrypted payload for sync.

Projection tests:

- locked/hidden projection excludes secret bookmarks;
- unlocked/visible projection includes decrypted secret bookmarks;
- corrupt secret payload is handled without exposing data;
- search index excludes secrets while hidden;
- search index includes secrets while visible;
- hiding secrets removes them from current search results.

UI/view-model tests:

- shortcut state transitions;
- first secret bookmark prompts password setup;
- canceling setup leaves bookmark normal;
- auto-hide after timeout hides secrets but keeps session unlocked;
- reveal after auto-hide does not ask for password again.

Manual checks:

- inspect SQLite with a secret bookmark and confirm title/url are not plaintext;
- verify app starts with secrets hidden;
- verify shortcut on macOS and non-macOS;
- verify inactivity auto-hide;
- verify password change;
- verify search behavior.

## Future Sync Considerations

Remote sync should synchronize encrypted payloads, not plaintext.

Remote sync object for a secret bookmark can include:

- item ID;
- parent ID;
- item type;
- encrypted payload;
- encryption nonce;
- crypto profile ID/version;
- timestamps/revisions/tombstone metadata.

Remote sync should not include:

- plaintext title;
- plaintext URL;
- plaintext search tokens.

Password change with DEK/KEK:

- if the same profile row is updated, sync must propagate updated wrapped DEK/password-check metadata;
- secret item payloads do not need to change.

Conflict copies:

- If a secret bookmark conflicts while locked, preserve both encrypted versions without needing plaintext.
- If conflict resolution needs UI comparison, require unlock.

## Implementation Preference Summary

Recommended first implementation:

1. Add `Notes/ENCRYPTION_IMPLEMENTATION_PLAN.md` as the step-by-step plan.
2. Add `Security/` services:
   - crypto profile records;
   - secret payload DTOs;
   - KDF service;
   - AES-GCM payload service;
   - secret session service.
3. Adjust SQLite crypto schema to support wrapped DEK.
4. Keep secret bookmark plaintext out of SQLite from the first persisted version.
5. Hide/show secrets through projection and search rebuild, not through view-only flags.
6. Add UI flows after crypto and projection tests exist.
