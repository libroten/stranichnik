# Selective Encryption Implementation Plan

This is the step-by-step implementation plan for selective encryption of secret bookmarks in Stranichnik.

Read `Notes/ENCRYPTION_ARCHITECTURE.md` first. This plan assumes the architecture described there.

Important: this plan is detailed so another agent can continue without reconstructing chat context. It is not a fixed contract. If implementation reveals a safer or simpler path, update this document and related Notes before continuing.

Current status: the main v1 implementation described by this document is implemented and manually verified. Keep this file as historical implementation context and as a checklist for future review, but use `Notes/ARCHITECTURE.md`, `Notes/PROJECT_STATE.md`, and `Notes/ENCRYPTION_ARCHITECTURE.md` for the current architecture summary.

## Project Rules

- Do not run `dotnet build`.
- Do not run `dotnet run`.
- Do not run `dotnet test`.
- Do not run `dotnet format`.
- Ask the user to run verification commands and paste output.
- Do not weaken analyzers, formatting, or code-quality rules without explicit user approval.
- Keep changes small and verifiable.
- Add logs where useful, but never log user data or cryptographic material.

Forbidden logs:

- master password;
- derived keys;
- DEK;
- KEK;
- salts;
- nonces;
- ciphertext;
- encrypted payload JSON;
- bookmark URLs;
- bookmark titles;
- folder titles;
- raw hashes;
- local file paths for user-selected data.

## Required Reading

Before implementation, read:

- `Notes/ARCHITECTURE.md`
- `Notes/PROJECT_STATE.md`
- `Notes/FUTURE_FEATURES_PLAN.md`
- `Notes/DATA_SCHEMA.md`
- `Notes/STORAGE_ARCHITECTURE.md`
- `Notes/SEARCH_ENGINE_ARCHITECTURE.md`
- `Notes/ENCRYPTION_ARCHITECTURE.md`

Useful current code areas:

- `Storage/BookmarkItemRecord.cs`
- `Storage/EncryptedBookmarkPayloadRecord.cs`
- `Storage/IBookmarkTreeStore.cs`
- `Storage/InMemoryBookmarkTreeStore.cs`
- `Storage/Sqlite/SqliteDatabaseMigrator.cs`
- `Storage/Sqlite/SqliteBookmarkTreeStore.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`
- `Views/BookmarkEditorDialog.axaml`
- `Views/BookmarkEditorDialog.axaml.cs`
- `Searching/`
- `Localization/UiStrings.cs`
- `Resources/Strings.resx`
- `Resources/Strings.ru.resx`

## Target User Behavior

The final first version should behave like this:

1. App starts with secret bookmarks hidden.
2. User can press:
   - macOS: `Cmd+P`;
   - Windows/Linux: `Ctrl+P`.
3. If the master password exists but has not been entered during this app run, the app asks for it.
4. If the password is correct, secret bookmarks become visible and searchable.
5. Pressing the shortcut again hides secret bookmarks.
6. Showing hidden secrets again during the same app run does not ask for the password again.
7. After one minute of no meaningful UI interaction, visible secret bookmarks are automatically hidden.
8. Auto-hide does not forget the already-unlocked runtime key.
9. If no master password exists and the user tries to make a bookmark secret, the app prompts to create the master password first.
10. The app supports changing the master password later.
11. If a folder contains only hidden secret bookmarks, that folder is hidden from the visible tree while secrets are hidden.

## Implementation Strategy

Implement in layers:

1. Crypto primitives and tests.
2. SQLite crypto profile schema.
3. Secret profile store operations.
4. Secret session state.
5. Secret payload projection.
6. Storage CRUD changes for secret bookmarks.
7. ViewModel/search integration.
8. UI dialogs and shortcut.
9. Inactivity auto-hide.
10. Documentation cleanup.

Do not start with UI. The crypto/storage/projection behavior must be testable without Avalonia windows first.

## Phase 1: Add Security Domain Types

Goal:

- add plain records/enums for crypto metadata and secret runtime state.

Create a new folder:

```text
Security/
```

Suggested files:

```text
Security/CryptoProfileRecord.cs
Security/CryptoProfileVersion.cs
Security/SecretBookmarkPayloadV1.cs
Security/SecretSessionState.cs
Security/SecretVisibilityState.cs
Security/SecretEncryptionConstants.cs
```

Suggested records:

```csharp
public sealed record CryptoProfileRecord(
    long Id,
    int ProfileVersion,
    string KdfName,
    string KdfHashAlgorithm,
    int KdfIterations,
    ReadOnlyMemory<byte> KdfSalt,
    int KekLengthBytes,
    string DataKeyAlgorithm,
    ReadOnlyMemory<byte> WrappedDataKey,
    ReadOnlyMemory<byte> WrappedDataKeyNonce,
    string EncryptionAlgorithm,
    string PayloadFormat,
    ReadOnlyMemory<byte> PasswordCheckPayload,
    ReadOnlyMemory<byte> PasswordCheckNonce,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
```

```csharp
public sealed record SecretBookmarkPayloadV1(
    string Title,
    string Url);
```

Suggested constants:

```text
KdfName = "PBKDF2"
KdfHashAlgorithm = "SHA256"
DefaultPbkdf2Iterations = 600000
KdfSaltLengthBytes = 32
KekLengthBytes = 32
DataKeyLengthBytes = 32
AesGcmNonceLengthBytes = 12
AesGcmTagLengthBytes = 16
EncryptionAlgorithm = "AES-256-GCM"
PayloadFormat = "stranichnik-secret-json-v1"
```

Notes:

- Keep domain records independent from Avalonia.
- Avoid `byte[]` mutation where possible; when using arrays for crypto APIs, clear sensitive arrays after use.
- `ReadOnlyMemory<byte>` is fine for persisted data records, but runtime key material needs explicit cleanup.

Tests:

- simple construction/validation tests if validation is added.

Ask the user to run:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

## Phase 2: Implement Low-Level Crypto Service

Goal:

- add isolated encryption/decryption code with no storage/UI dependencies.

Suggested files:

```text
Security/ISecretCryptoService.cs
Security/SecretCryptoService.cs
Security/SecretCryptoException.cs
Security/SecretCryptoFailureReason.cs
```

Suggested API:

```csharp
public interface ISecretCryptoService
{
    CryptoProfileCreationResult CreateProfile(string masterPassword, DateTimeOffset nowUtc);

    SecretUnlockResult Unlock(CryptoProfileRecord profile, string masterPassword);

    EncryptedSecretPayload EncryptBookmarkPayload(
        SecretBookmarkPayloadV1 payload,
        RuntimeSecretKey dataKey,
        long cryptoProfileId,
        string itemId);

    SecretBookmarkPayloadV1 DecryptBookmarkPayload(
        EncryptedBookmarkPayloadRecord encryptedPayload,
        RuntimeSecretKey dataKey,
        string itemId);

    CryptoProfileRecord ChangeMasterPassword(
        CryptoProfileRecord profile,
        RuntimeSecretKey dataKey,
        string newMasterPassword,
        DateTimeOffset nowUtc);
}
```

Suggested result records:

```csharp
public sealed record CryptoProfileCreationResult(
    CryptoProfileRecord Profile,
    RuntimeSecretKey DataKey);

public sealed record SecretUnlockResult(
    bool IsSuccess,
    RuntimeSecretKey? DataKey,
    SecretCryptoFailureReason? FailureReason);
```

Runtime key handling:

- Add `RuntimeSecretKey : IDisposable`.
- It owns a private `byte[]`.
- It exposes key material only as `ReadOnlySpan<byte>` during crypto calls.
- `Dispose()` clears the array through `CryptographicOperations.ZeroMemory`.
- Never expose the array publicly.

Crypto details:

- Generate DEK with `RandomNumberGenerator.GetBytes(32)`.
- Generate PBKDF2 salt with `RandomNumberGenerator.GetBytes(32)`.
- Derive KEK with `Rfc2898DeriveBytes.Pbkdf2`.
- Wrap DEK with AES-GCM using KEK.
- Generate a separate random 12-byte nonce for every AES-GCM encryption.
- Use 16-byte authentication tags.
- Store tag and ciphertext in a JSON envelope or in clearly separated fields. The architecture recommends compact JSON envelope in `encrypted_payload` and nonce in the row/profile.
- Use AAD strings described in `Notes/ENCRYPTION_ARCHITECTURE.md`.

Password-check payload:

- Encrypt a fixed non-secret marker with KEK, for example:

```text
stranichnik-password-check-v1
```

- Decrypting this marker during unlock verifies the password.
- Do not use a separate password hash unless needed later; the wrapped DEK/password-check AES-GCM authentication already proves the KEK is correct.

Failure handling:

- Wrong password, corrupted wrapped DEK, corrupted password check, and unsupported profile should all become safe failure results.
- Do not throw raw cryptographic exceptions across application boundaries.
- Tests can assert specific failure reasons.

Tests:

- creating profile produces non-empty fields;
- correct password unlocks;
- wrong password fails;
- changing password makes old password fail;
- changing password makes new password succeed;
- changing password keeps data key able to decrypt old bookmark payloads;
- same payload encrypted twice has different nonce/ciphertext;
- corrupt payload fails;
- corrupt tag fails;
- wrong item ID/AAD fails;
- runtime key `Dispose()` can be called multiple times safely.

Ask the user to run verification commands.

## Phase 3: Update SQLite Crypto Schema

Goal:

- make `crypto_profiles` support wrapped DEK and profile metadata.

Current schema already contains a draft `crypto_profiles` table, but it is not sufficient for DEK/KEK.

Add a new migration, likely schema version 3.

Options:

1. If no released user data exists, rebuild `crypto_profiles` table in-place.
2. If preserving old local databases matters, perform a safe migration:
   - create `crypto_profiles_new`;
   - copy compatible rows if any;
   - drop old table;
   - rename new table.

Since the feature has not been used yet, option 1 or a simple rebuild is acceptable, but be careful not to break existing `items` foreign key definitions.

Recommended final table:

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

    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
```

Also add to `items`:

```sql
secret_payload_format_version INTEGER NULL
```

Update CHECK constraints if practical.

If SQLite cannot add/change CHECK constraints cleanly:

- rebuild the `items` table in a migration;
- preserve existing rows;
- keep the migration explicit and tested.

Tests:

- new database reaches version 3;
- old version 1/2 test database migrates to version 3;
- `crypto_profiles` has the expected columns;
- `items.secret_payload_format_version` exists;
- normal existing rows remain loadable after migration.

Update:

- `Notes/DATA_SCHEMA.md`
- `Notes/STORAGE_ARCHITECTURE.md` if store responsibilities change.

Ask user to run verification commands.

## Phase 4: Add Crypto Profile Store Methods

Goal:

- persist and load the active crypto profile.

Do not overload `IBookmarkTreeStore` too much if the interface starts feeling muddy.

Preferred option:

- add a new interface:

```csharp
public interface ISecretProfileStore
{
    CryptoProfileRecord? LoadActiveProfile();
    CryptoProfileRecord SaveNewProfile(CryptoProfileRecord profile);
    CryptoProfileRecord UpdateProfile(CryptoProfileRecord profile);
}
```

Implementation:

- `SqliteSecretProfileStore` or methods inside `SqliteBookmarkTreeStore` if keeping the implementation smaller.
- In-memory test implementation.

Open issue:

- There is only one master password/profile in the first version.
- `crypto_profiles` can contain one row with `id = 1`, or app metadata can store `active_crypto_profile_id`.

Recommendation:

- Use one row with `id = 1` for first version.
- This is simple and can be migrated later if multiple profiles ever matter.

Tests:

- no profile returns `null`;
- save profile can be loaded exactly;
- update profile changes KDF salt/wrapped DEK/password check;
- persisted binary values round-trip.

Ask user to run verification commands.

## Phase 5: Implement Secret Session Service

Goal:

- centralize current run lock/unlock/visibility state.

Suggested files:

```text
Security/ISecretSessionService.cs
Security/SecretSessionService.cs
Security/SecretSessionChangedEventArgs.cs
```

Suggested state:

```csharp
public enum SecretSessionStatus
{
    NotConfigured,
    ConfiguredLocked,
    ConfiguredUnlockedHidden,
    ConfiguredUnlockedVisible
}
```

Suggested API:

```csharp
public interface ISecretSessionService : IDisposable
{
    SecretSessionStatus Status { get; }
    bool IsConfigured { get; }
    bool IsUnlocked { get; }
    bool AreSecretsVisible { get; }

    event EventHandler? StateChanged;

    void MarkNotConfigured();
    void MarkConfiguredLocked();
    void ConfigureAndUnlock(RuntimeSecretKey dataKey, bool showSecrets);
    void Unlock(RuntimeSecretKey dataKey, bool showSecrets);
    void ShowSecrets();
    void HideSecrets();
    void LockAndForgetKey();
    RuntimeSecretKey? BorrowDataKey();
}
```

Runtime key policy:

- The service owns the key.
- Borrowing should not transfer ownership.
- Do not allow callers to store raw key bytes.
- On app shutdown, dispose service and zero key.
- `MarkConfiguredLocked()` is used on startup when the crypto profile exists in storage, but the user has not entered the master password during this app run.

Tests:

- initial status transitions;
- show/hide after unlock does not dispose key;
- lock disposes/forgets key;
- configure from first password setup;
- events fire only on actual state changes.

Ask user to run verification commands.

## Phase 6: Implement Secret Payload Projection

Goal:

- convert storage snapshots into visible/decrypted snapshots for tree/search.

Suggested files:

```text
Security/SecretBookmarkProjectionService.cs
Security/SecretProjectionResult.cs
```

Suggested API:

```csharp
public sealed class SecretBookmarkProjectionService
{
    public SecretProjectionResult Project(
        BookmarkTreeSnapshot storageSnapshot,
        SecretSessionService session);
}
```

Behavior:

- If session is locked or secrets hidden:
  - exclude secret bookmark records from the projected snapshot.
  - hide folders that become empty only because their secret descendants were excluded.
- If session is unlocked and secrets visible:
  - decrypt secret bookmark records;
  - return records with `Title` and `Url` filled for UI/search projection;
  - keep `IsSecret = true`;
  - keep original encrypted metadata if needed.
- If a secret record cannot be decrypted:
  - exclude it from visible projection;
  - record a safe projection warning without plaintext.

Parent/folder behavior:

- Secret bookmarks can live under normal folders.
- If a folder has only hidden secret bookmarks, the folder itself is hidden while secrets are hidden.
- If a folder has only child folders that become hidden by the same rule, the parent folder is also hidden.
- Genuinely empty user-created folders remain visible.
- If a folder still has at least one visible child, it remains visible.
- Since folders are not secret in v1, folder titles are not decrypted or redacted. Only projection-level folder visibility can change when all descendants are hidden secrets.

Tests:

- hidden projection removes secret records;
- hidden projection removes folders that contain only hidden secret descendants;
- hidden projection keeps genuinely empty folders visible;
- hidden projection keeps folders visible when they still have at least one visible child;
- visible projection decrypts secret records;
- corrupt secret payload does not crash;
- normal records are unchanged;
- record order/parent IDs are preserved.

Ask user to run verification commands.

## Phase 7: Extend Store CRUD For Secret Bookmarks

Goal:

- allow adding/editing secret bookmarks through storage and view model paths.

Do this after crypto and projection tests exist.

Possible store API changes:

Option A: add specific secret methods:

```csharp
BookmarkItemRecord AddSecretBookmarkToFolderStart(
    string? parentId,
    string bookmarkId,
    EncryptedBookmarkPayloadRecord encryptedPayload);

BookmarkItemRecord EditBookmarkAsSecret(
    string bookmarkId,
    EncryptedBookmarkPayloadRecord encryptedPayload);

BookmarkItemRecord EditSecretBookmarkAsPlaintext(
    string bookmarkId,
    string title,
    string url);
```

Option B: use a command record:

```csharp
public sealed record BookmarkWriteContent(
    string? Title,
    string? Url,
    bool IsSecret,
    EncryptedBookmarkPayloadRecord? EncryptedPayload,
    int? SecretPayloadFormatVersion);
```

Recommendation:

- Use specific methods first. They are explicit and harder to misuse.
- Refactor to command records later if the API grows too much.

Rules:

- Secret bookmarks must have no plaintext title/url in storage.
- Secret bookmarks have no plaintext `icon_asset_id`.
- Secret bookmarks may use encrypted custom/fetched icons through `secret_icon_asset_id`.
- The encrypted secret icon design is documented in `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md`.
- Converting to secret moves/copies an existing regular icon to encrypted secret icon storage when possible.
- Converting to normal moves/copies an existing secret icon back to regular icon storage when possible.
- Converting to normal clears encrypted payload fields.
- Only bookmarks can become secret.
- Folders remain non-secret.

Tests:

- add secret bookmark stores title/url as null;
- add secret bookmark stores encrypted payload fields;
- add secret bookmark can use encrypted custom/fetched icon;
- edit normal to secret clears title/url/plaintext icon and can preserve the icon as encrypted secret icon;
- edit secret to normal restores title/url, clears encrypted fields, and can preserve the icon as regular icon;
- edit folder as secret is rejected;
- move secret bookmark works without decrypting;
- delete secret bookmark tombstones without plaintext leakage.

Ask user to run verification commands.

## Phase 8: ViewModel And Search Integration

Goal:

- make visible tree/search respond to secret visibility state.

Current `MainWindowViewModel` owns store and search coordination.

Recommended first implementation:

- Add dependencies:
  - `ISecretSessionService`;
  - `SecretBookmarkProjectionService`;
  - `ISecretCryptoService`;
  - profile store/service.
- Keep a raw storage snapshot in memory if needed.
- Add a method:

```csharp
public void ReloadVisibleTreeAndSearch();
```

Behavior:

- Load storage snapshot.
- Project visible snapshot based on secret session state.
- Rebuild tree view models from projected snapshot.
- Rebuild search index from projected visible bookmarks.
- Preserve expansion state where practical.

Expansion-state preservation:

- Existing code likely stores expansion state in view models.
- Rebuilding tree can collapse everything if not handled.
- Add a helper to capture expanded folder IDs before rebuild and reapply after rebuild.
- Root should remain expanded/collapsed according to previous state if possible.

Search:

- When secrets become visible, rebuild search from projected visible snapshot.
- When secrets become hidden, rebuild search without secret bookmarks.
- Refresh current `SearchResults` for the existing `SearchQuery`.

Tests:

- hidden secrets absent from `RootFolder`;
- visible secrets appear;
- search excludes/includes based on visibility;
- hide after visible removes current search result;
- expansion state is preserved where possible.

Ask user to run verification commands.

## Phase 9: Password Setup Dialog

Goal:

- support first-use master password creation.

Suggested files:

```text
Views/SetMasterPasswordDialog.axaml
Views/SetMasterPasswordDialog.axaml.cs
```

UI requirements:

- project-owned styling;
- no default Avalonia theme leakage;
- two password fields:
  - password;
  - repeat password;
- clear warning that there is no password recovery;
- confirm action;
- cancel action;
- close with `Esc`.

Validation:

- password not empty;
- repeat matches;
- optional minimum length guidance but do not block too aggressively unless user asks.

Flow:

- Trigger from bookmark editor when enabling "Secret" and no profile exists.
- On cancel, revert secret toggle to off.
- On success, create crypto profile, save it, unlock runtime session.

Localization:

- add all user-facing strings to `.resx` and `UiStrings`.

Tests:

- view-model/dialog result tests where practical.
- service-level setup flow should have unit tests independent from UI.

Ask user to run verification commands and manually inspect dialog.

## Phase 10: Unlock Dialog And Shortcut

Goal:

- show/hide secrets with `Cmd+P` or `Ctrl+P`.

Suggested files:

```text
Views/UnlockSecretsDialog.axaml
Views/UnlockSecretsDialog.axaml.cs
```

Shortcut implementation:

- In `MainWindow.axaml.cs` `OnKeyDown`.
- macOS:
  - `Key.P` + `KeyModifiers.Meta`.
- Windows/Linux:
  - `Key.P` + `KeyModifiers.Control`.
- Determine OS through `OperatingSystem.IsMacOS()` or equivalent.

Shortcut behavior:

- `NotConfigured`: show message or no-op. Recommendation: show friendly message only if user explicitly pressed shortcut.
- `ConfiguredLocked`: open unlock dialog.
- `ConfiguredUnlockedHidden`: show secrets, rebuild projection/search.
- `ConfiguredUnlockedVisible`: hide secrets, rebuild projection/search.

Unlock dialog:

- one password field;
- confirm/cancel;
- close with `Esc`;
- wrong password shows generic error.

Important:

- Do not trigger shortcut from inside password dialogs.
- Do not log password values.

Tests:

- shortcut state transitions in view model/service where possible;
- unlock success/failure service tests;
- manual test for keyboard shortcut.

Ask user to run verification commands and manually inspect app.

## Phase 11: Inactivity Auto-Hide

Goal:

- hide visible secret bookmarks after one minute without UI activity.

Suggested files:

```text
Security/SecretInactivityMonitor.cs
```

Or implement in `MainWindow.axaml.cs` first if smaller.

Recommended API:

```csharp
public sealed class SecretInactivityMonitor
{
    public event EventHandler? Timeout;
    public void Start();
    public void Stop();
    public void NotifyActivity();
}
```

Timer:

- `DispatcherTimer`
- interval: 1 minute

Activity events:

- key down;
- pointer pressed;
- pointer released;
- pointer wheel;
- menu commands;
- context menu commands;
- dialog completion callbacks;
- drag/drop operations;
- search text changes.

Do not reset timer from passive hover in v1.

Timeout behavior:

- if secrets are visible:
  - hide secrets;
  - rebuild visible tree/search;
  - keep session unlocked;
  - log generic timeout event.
- if secrets are already hidden:
  - no-op.

Tests:

- timer service can be tested with injectable clock/timer abstraction if practical;
- otherwise test state transition method directly and manually inspect timer behavior.

Ask user to run verification commands and manual test:

1. unlock/show secrets;
2. wait one minute without interaction;
3. verify secrets hide;
4. press shortcut again;
5. verify secrets show without password prompt.

## Phase 12: Bookmark Editor Secret Toggle

Goal:

- allow creating/editing secret bookmarks.

UI:

- Add "Secret bookmark" checkbox/toggle for bookmarks only.
- Do not show it for folders.
- When secret is enabled:
  - custom/favicons are hidden or disabled;
  - a neutral built-in/default icon is used;
  - title metadata suggestion can still work before save, but fetched title/URL must not be logged.

Flow when no master password:

1. User turns on secret toggle.
2. Show set master password dialog.
3. If success:
   - leave secret toggle on;
   - session unlocked;
   - continue editing.
4. If cancel:
   - turn secret toggle off.

Flow when configured but locked:

1. User turns on secret toggle or edits existing secret bookmark.
2. Show unlock dialog.
3. If success:
   - continue.
4. If cancel/fail:
   - do not enable secret editing.

Flow when editing existing secret bookmark:

- Existing secret bookmark can only be opened for editing while secrets are visible.
- Editor receives decrypted title/url from the view model.
- Saving re-encrypts payload with a new nonce.

Tests:

- enabling secret with no profile starts setup flow;
- cancel setup leaves item normal;
- saving secret calls secret store path;
- converting secret to normal calls plaintext path.

Manual checks:

- create first secret bookmark;
- close/reopen app and verify hidden;
- unlock and edit secret bookmark;
- convert normal to secret;
- convert secret to normal.

## Phase 13: Change Master Password UI

Goal:

- support changing the master password.

Menu location:

- `Stranichnik -> Settings`.
- The settings window is scrollable.
- The first settings section is `Secret bookmarks`.
- Sections are separated visually with a title and separator.

Window:

```text
Views/SettingsDialog.axaml
Views/SettingsDialog.axaml.cs
```

Flow:

- If no profile exists:
  - show setup password dialog instead.
- If session locked:
  - ask current password first or reuse unlock dialog.
- If session unlocked:
  - optionally skip current password for this run.
  - Architecture recommendation: skip current password because the user already unlocked this run.
- Ask for new password and repeat inside the `Secret bookmarks` settings section.
- Derive new KEK with new salt.
- Re-wrap existing DEK.
- Generate new password-check payload.
- Update profile in a transaction.

Tests:

- password change succeeds;
- old password no longer unlocks;
- new password unlocks;
- existing secret payload decrypts after password change;
- failed update does not corrupt active profile.

Ask user to run verification commands and manual test.

## Phase 14: Logs And Privacy Review

Goal:

- ensure no user data or crypto material leaks.

Checklist:

- Search for `Logs.Print` in touched files.
- Confirm no URLs/titles/passwords/keys/salts/nonces/ciphertext are logged.
- Confirm metadata fetch logs still avoid URLs/titles.
- Confirm clipboard logs do not include copied URL.
- Confirm encryption logs are generic.

Suggested safe log messages:

```text
Secret crypto profile created.
Secret unlock succeeded.
Secret unlock failed.
Secrets shown.
Secrets hidden by user action.
Secrets hidden by inactivity timeout.
Master password change completed.
Master password change failed.
Secret projection rebuild completed.
```

Avoid counts unless the user approves that counts are not sensitive.

## Phase 15: Documentation Cleanup

Update:

- `Notes/ARCHITECTURE.md`
- `Notes/PROJECT_STATE.md`
- `Notes/FUTURE_FEATURES_PLAN.md`
- `Notes/DATA_SCHEMA.md`
- `Notes/STORAGE_ARCHITECTURE.md`
- `Notes/ENCRYPTION_ARCHITECTURE.md`
- `Notes/ENCRYPTION_IMPLEMENTATION_PLAN.md`

README:

- Update only if clone/build/run/test instructions change.
- If a new command-line option is added, document it if user-facing.

## Suggested Implementation Slices

Recommended small PR/commit slices:

1. Security domain records and crypto service tests.
2. SQLite crypto profile schema migration and tests.
3. Profile store and session service.
4. Secret projection service and tests.
5. Store methods for secret bookmark add/edit.
6. ViewModel/search rebuild integration.
7. Password setup/unlock dialogs and shortcut.
8. Inactivity auto-hide.
9. Bookmark editor secret toggle.
10. Change password UI.
11. Documentation update.

Each slice should end with the user running:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

For UI slices, also ask the user to run:

```bash
dotnet run -- --print-logs-to-console
```

## Manual End-To-End Acceptance Checklist

Before considering the first encryption feature complete:

1. Fresh app with no password:
   - normal bookmarks work;
   - shortcut gives friendly behavior;
   - first secret bookmark prompts password setup.
2. Create secret bookmark:
   - save succeeds;
   - SQLite does not contain plaintext title/url.
3. Restart app:
   - secret bookmark is hidden.
4. Unlock via shortcut:
   - password prompt appears;
   - correct password shows secret bookmark;
   - search finds secret bookmark only while visible.
5. Hide via shortcut:
   - secret bookmark disappears;
   - search result disappears;
   - pressing shortcut again shows it without password.
6. Auto-hide:
   - after one minute of no interaction, secret bookmark disappears;
   - pressing shortcut shows it without password.
7. Wrong password:
   - unlock fails safely;
   - no secret data is shown.
8. Edit secret bookmark:
   - title/url update works;
   - SQLite still has no plaintext.
9. Convert normal to secret:
   - SQLite clears plaintext title/url;
   - custom icon is cleared.
10. Convert secret to normal:
   - plaintext title/url returns to SQLite by design;
   - bookmark behaves like normal.
11. Change master password:
   - old password no longer unlocks;
   - new password unlocks;
   - existing secret bookmarks remain readable.
12. Logs:
   - no bookmark titles;
   - no URLs;
   - no password/key/salt/nonce/ciphertext.
