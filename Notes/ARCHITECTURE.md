# Stranichnik Architecture

This note is for future agents working on the local project. It describes the current shape of the codebase and the main design decisions.

## Project Type

Stranichnik is a cross-platform desktop bookmark manager written in C# with Avalonia UI.

Target framework:

- `net8.0`

UI framework:

- Avalonia UI

Current storage:

- SQLite is the active local application storage.
- The SQLite database file is `stranichnik.sqlite` under the app data directory.
- New databases are empty by default.
- Running with `--use-sample-data` seeds sample bookmarks only when the SQLite file did not exist before startup.
- `InMemoryBookmarkTreeStore` remains as a contract/reference implementation for tests.
- The SQLite schema is documented in `Notes/DATA_SCHEMA.md`.
- The storage/repository architecture is documented in `Notes/STORAGE_ARCHITECTURE.md`.
- Search currently uses the standalone in-memory `Stranichnik.Search` library.
- Future WebDAV sync should synchronize item-level objects, not the SQLite database file itself.

## Main UI Structure

The main window is implemented in:

- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`

Current dialog windows:

- `Views/BookmarkEditorDialog.axaml`
- `Views/BookmarkEditorDialog.axaml.cs`
- `Views/ConfirmDialog.axaml`
- `Views/ConfirmDialog.axaml.cs`
- `Views/LanguageDialog.axaml`
- `Views/LanguageDialog.axaml.cs`
- `Views/MessageDialog.axaml`
- `Views/MessageDialog.axaml.cs`
- `Views/AppearanceDialog.axaml`
- `Views/AppearanceDialog.axaml.cs`
- `Views/SetMasterPasswordDialog.axaml`
- `Views/SetMasterPasswordDialog.axaml.cs`
- `Views/UnlockSecretsDialog.axaml`
- `Views/UnlockSecretsDialog.axaml.cs`
- `Views/SettingsDialog.axaml`
- `Views/SettingsDialog.axaml.cs`

The UI is a tree-like bookmark catalog:

- A synthetic root folder named `Все закладки`.
- Folder rows can be expanded and collapsed.
- On initial tree creation, when no explicit expanded-folder state is supplied,
  the UI expands the first two real folder levels below the synthetic root.
  The synthetic root itself is not counted as one of those two levels.
- Bookmark rows show title and URL.
- A search bar can replace the tree area with bookmark search results.
- Folder and bookmark rows have hover actions.
- Folder and bookmark rows also have right-click context menus.
- Folders have container borders on the left and bottom.
- DnD placeholders appear inside folders while dragging.
- Bookmark URLs can be opened in the system browser.
- Bookmark URLs can be copied to the system clipboard from the context menu.
- Bookmark URLs can also be opened from search results.

The synthetic root folder is special:

- It is visible as a normal folder row.
- It can be expanded and collapsed.
- It cannot be dragged.
- It cannot be edited or deleted.
- It has always-visible add buttons.
- Its context menu contains only add bookmark and add folder actions.
- The root drop placeholder is inside this folder, not above the tree.

Dialogs:

- `BookmarkEditorDialog` handles add/edit for both bookmarks and folders.
- `BookmarkEditorDialog` can mark bookmarks as secret. When the secret session is unlocked, secret bookmark editing supports the same icon choices as normal bookmark editing.
- For bookmark add/edit, `BookmarkEditorDialog` can fetch page metadata from the entered URL and show the discovered page title as a clickable suggestion.
- `ConfirmDialog` handles delete confirmation for bookmarks and folders.
- `LanguageDialog` handles choosing the application UI language.
- `MessageDialog` handles one-button messages, currently used when opening a page fails.
- `AppearanceDialog` handles choosing the application theme.
- `SetMasterPasswordDialog` handles first-time secret master password setup.
- `UnlockSecretsDialog` handles secret bookmark unlock.
- `SettingsDialog` is scrollable and currently contains the first secret-bookmark settings section for changing the master password.
- Dialogs use `Esc` as cancel/close behavior.

Inline dialog status messages:

- `Views/StatusBanner.axaml` is the shared component for inline validation errors, operation errors, and success messages inside dialogs.
- Use `StatusBanner.ShowError(...)`, `StatusBanner.ShowSuccess(...)`, and `StatusBanner.Hide()` from dialog code-behind.
- Prefer `StatusBanner` over raw `TextBlock` error/status labels in form dialogs.
- The banner wraps long localized text, uses project theme brushes, and keeps message styling consistent.
- Keep `MessageDialog` for separate modal messages that are not part of an existing form.

Menus:

- The top menu row is custom-styled in `Views/MainWindow.axaml`.
- Tree rows have a custom right-click context menu implemented in `Views/MainWindow.axaml` and `Views/MainWindow.axaml.cs`.
- The tree context menu intentionally uses the same visual language as the top popup menus.
- Popup menu shadows are drawn with `BoxShadow` on the menu surface inside a transparent padded host. The padded host closes the popup when clicked outside the actual menu surface.
- This avoids relying on platform-specific popup window shadows, which may not render consistently across macOS, Windows, and Linux.
- Bookmark context menus contain go/open, copy URL, edit, and delete actions.
- Folder context menus contain add bookmark, add folder, edit, and delete actions.
- The synthetic root folder context menu contains only add bookmark and add folder.

URL opening:

- Implemented in `Views/MainWindow.axaml.cs`.
- Uses `ProcessStartInfo` with `UseShellExecute = true`.
- Only absolute `http` and `https` URLs are accepted.
- Invalid URLs and system launch errors show `MessageDialog` instead of failing silently.
- User-entered web addresses without a scheme are normalized before opening when they look like web addresses.
- The open action is labeled as "Go" / "Перейти" in the UI.

Clipboard:

- Bookmark URL copying is implemented in `Views/MainWindow.axaml.cs`.
- It uses Avalonia's top-level clipboard abstraction so it remains cross-platform.
- Clipboard diagnostics must not log copied URL values.

Bookmark metadata fetching:

- Implemented in `Opening/BookmarkMetadataFetcher.cs` and `Opening/BookmarkMetadataParser.cs`.
- The main app uses AngleSharp for normal HTML metadata parsing.
- `BookmarkMetadataFetcher` reuses `BookmarkUrlNormalizer` rules and supports typed web addresses without an explicit scheme.
- The fetcher requests only HTML/XHTML, uses a bounded timeout, supports cancellation, and reads a bounded response prefix.
- The fetcher extracts titles in this order:
  - `meta[property="og:title"]`
  - `meta[name="twitter:title"]`
  - `<title>`
- It also extracts favicon candidates from supported `<link rel="...">` tags and adds a `/favicon.ico` fallback candidate.
- It downloads only a bounded number of favicon candidates, with a separate favicon byte limit.
- Downloaded favicons are processed in memory and exposed to `BookmarkEditorDialog` as a pending option; they are persisted only if the user saves the dialog with that icon selected.
- If the response exceeds the configured HTML size limit, the fetcher does not keep reading the full response. It tries a partial fallback by searching the already downloaded prefix for:
  - a complete `<title>...</title>`;
  - supported favicon `<link ...>` tags.
- Metadata fetching logs diagnostic states, but must not log bookmark URLs or discovered titles.

Icons:

- Default icon visuals are drawn in XAML/vector UI and are selected by item kind and current theme.
- Custom and favicon icons are stored as immutable processed PNG blobs in SQLite table `icon_assets`.
- Bookmark/folder rows reference custom icons through nullable `items.icon_asset_id`.
- `items.icon_asset_id = NULL` means "use the default icon".
- Icon processing code lives in `IconProcessing/` with namespace `Stranichnik.Icons`.
- `IconAssetService` hashes original source bytes with SHA-256, reuses an existing asset with the same source hash, or creates a new immutable icon asset.
- `IconImageProcessor` normalizes source images into `64x64` PNG with aspect ratio preserved.
- `BookmarkIconImageCache` decodes stored icon blobs lazily and avoids repeated SQLite blob decoding.
- Secret bookmark custom icons are stored separately in encrypted `secret_icon_assets` rows and referenced through `items.secret_icon_asset_id`.
- Visible unlocked secret bookmarks can show decrypted custom icons; hidden secret bookmarks are absent from the UI.
- `BookmarkIconImageCache` keeps decrypted secret icon bitmaps only in memory and clears that cache when the secret session changes.
- `BookmarkEditorDialog` lets the user choose current/default/favicon/uploaded icon options.
- Uploaded icon files use Avalonia's cross-platform storage provider.
- Dialog code never writes icon blobs directly to SQLite. It returns a pending `BookmarkIconSelection`; `MainWindowViewModel` applies it through `IconAssetService` and `IBookmarkTreeStore`.
- Secret icon selections are applied through `SecretIconAssetService`, which encrypts processed icon bytes with the runtime secret key.
- Icon logs must not include URLs, page titles, local file paths, raw hashes, or raw image data.

Search UI:

- Implemented in `Views/MainWindow.axaml` and `Views/MainWindow.axaml.cs`.
- The search bar is shown above the main bookmark work area.
- When search is inactive, the bookmark tree is visible.
- When search is active, the tree is hidden and the same work area displays search results or an empty state.
- Search result rows show title, URL, and an open action.
- The search result URL hover style matches normal bookmark URL hover behavior.
- The clear button resets `MainWindowViewModel.SearchQuery`.

## App Data And Logging

Shared application data paths are defined in:

- `Settings/AppDataPaths.cs`

Current app data files:

- `settings.json`
- `stranichnik.log`
- `stranichnik.sqlite`

`Settings/AppSettingsService.cs` stores user settings in `settings.json`.

Logging is implemented in:

- `Diagnostics/Logs.cs`

Use:

```csharp
Logs.Print("Message");
```

Current logging behavior:

- The log file is `stranichnik.log` in the same app data directory as `settings.json`.
- The log file is cleared on each application start, so it contains only the last run.
- `Program.Main` configures logging before Avalonia startup.
- `Program.Main` writes startup/shutdown messages.
- Startup logging includes the app data directory path.
- Running with `--print-logs-to-console` duplicates log lines to stdout.

Startup logging includes the SQLite database path, whether sample data was requested, and whether migrations/sample seeding ran. Metadata fetching logs success/failure states and fallback usage. Do not log bookmark titles, discovered page titles, or URLs.

Do not treat the current logger as a complete telemetry system. It is intentionally small and local, useful for diagnostics during development and future sync work.

## UI Styling Direction

The app uses a custom project-owned visual style with light and dark themes. For visually important controls, prefer explicit local styles and custom `ControlTemplate`s over relying on Avalonia's default themed templates.

Avalonia built-in controls may still be used for behavior, layout, focus handling, popups, scrolling, and accessibility. However, hover/pressed/focused backgrounds, borders, foregrounds, spacing, and popup surfaces should be controlled by project styles so the default Avalonia theme palette does not leak into the UI.

Theme support is implemented by Stranichnik, not by Avalonia's built-in light/dark theme switching:

- Do not use `RequestedThemeVariant` as the app theme state.
- Do not use Avalonia `ThemeVariant.Light` or `ThemeVariant.Dark` as the app theme model.
- `System` mode may read Avalonia `Application.ActualThemeVariant` as an OS
  theme signal, but the effective colors still come from project-owned
  `ThemePalettes.Light` / `ThemePalettes.Dark`.
- Keep the app theme mode in `Settings/AppSettings.cs`.
- Apply theme palettes through `Theming/ThemeService.cs`.
- Use semantic `DynamicResource` brushes in XAML for theme-dependent colors.

Theme implementation files:

- `Theming/ThemeMode.cs`
- `Theming/ThemeResourceKeys.cs`
- `Theming/ThemePalette.cs`
- `Theming/ThemePalettes.cs`
- `Theming/ThemeService.cs`

Theme switching UI:

- `Service -> Appearance` opens `Views/AppearanceDialog.axaml`.
- `Stranichnik -> Settings` opens `Views/SettingsDialog.axaml`.
- The appearance dialog supports `System`, `Light`, and `Dark`.
- The selected theme is saved in `settings.json` and applied immediately.

## Localization Direction

The app should support localization for all user-visible UI text that belongs to the application interface.

Chosen direction:

- Use `.resx` resource files as the source of localized UI strings.
- Use English as the default/fallback language in the neutral resource file.
- Add Russian as a localized resource file.
- Keep user data out of localization. Folder names, bookmark titles, URLs, future notes, and tags are user data and must remain unchanged.
- Localize interface text: menu items, button text, tooltips, dialog titles, dialog labels, validation messages, placeholders, error messages, and confirmation text.
- Add a thin localization service/adapter so the app can later support switching language from the UI, likely from `Service -> Language`.
- New UI strings should not be hardcoded directly in XAML or code-behind unless they are temporary during an active refactor.

Current implementation:

- Neutral/default resources live in `Resources/Strings.resx` and are English.
- Russian resources live in `Resources/Strings.ru.resx`.
- `Localization/TextResources.cs` wraps `ResourceManager` access.
- `Localization/UiStrings.cs` exposes strongly named properties/methods for UI code and XAML.
- `Localization/LanguageService.cs` normalizes and applies supported languages.
- `Settings/AppSettingsService.cs` persists the selected language in a local `settings.json` file under the user's application data folder.
- `Service -> Language` opens `LanguageDialog`.
- Language changes are saved immediately, but existing XAML created with `x:Static` is fully refreshed only after app restart.

Implementation preference:

- Start with ResX because it is the standard .NET localization mechanism and is also documented by Avalonia.
- XAML may reference generated resource properties for static text.
- Code-behind should access localized strings through a small helper/service rather than scattering `ResourceManager` calls everywhere.
- Dynamic runtime language switching remains a possible later improvement. The current implementation intentionally accepts app restart for full UI refresh.

Supported languages:

- English (`en`)
- Russian (`ru`)

## View Models

The main view model is:

- `ViewModels/MainWindowViewModel.cs`

Important types:

- `MainWindowViewModel`
- `BookmarkTreeItemViewModel`
- `BookmarkFolderViewModel`
- `BookmarkViewModel`

`MainWindowViewModel` owns:

- `RootFolder`: synthetic root folder.
- `Items`: alias for `RootFolder.Children`.
- `IBookmarkTreeStore`: storage boundary for tree data. The default runtime implementation is SQLite.
- `BookmarkSearchService`: application-side search boundary backed by `Stranichnik.Search`.
- Secret bookmark services for crypto profile setup, unlock, visibility projection, and master password changes.
- `SearchQuery` and `SearchResults`: current search UI state.

`BookmarkTreeItemViewModel` is the common base for folders and bookmarks. It currently stores:

- `Id`
- `Title`
- `Parent`
- `IconImage`
- DnD visual state flags.

`BookmarkFolderViewModel` stores:

- `Children`
- `IsExpanded`
- `ExpansionGlyph`
- root/action visibility flags.
- DnD placeholder and hover state flags.

`BookmarkViewModel` stores:

- `Url`
- `IsSecret`

## Secret Bookmarks

Selective bookmark encryption is implemented for bookmark title and URL payloads.

Important files:

- `Security/SecretCryptoService.cs`
- `Security/SecretSessionService.cs`
- `Security/SecretBookmarkProjectionService.cs`
- `Security/SecretProfileSetupService.cs`
- `Security/SecretUnlockService.cs`
- `Security/SecretMasterPasswordChangeService.cs`
- `Storage/ISecretProfileStore.cs`
- `Storage/InMemorySecretProfileStore.cs`
- `Storage/Sqlite/SqliteSecretProfileStore.cs`

Current crypto shape:

- The app uses application-level encryption for secret bookmark payloads, not whole-database encryption.
- A secret crypto profile is stored in SQLite table `crypto_profiles`.
- Each secret crypto profile has a `secret_generation_id` for master-password reset and future sync.
- The current profile uses PBKDF2-SHA256 with a per-profile salt and AES-256-GCM.
- The implementation uses a DEK/KEK model:
  - a random Data Encryption Key encrypts bookmark payloads;
  - a Key Encryption Key is derived from the master password;
  - the KEK wraps the DEK in the crypto profile;
  - changing the master password rewraps the DEK and does not re-encrypt every bookmark payload.
- `items.encrypted_payload`, `items.encryption_nonce`, `items.crypto_profile_id`, and `items.secret_payload_format_version` store encrypted bookmark payload metadata.
- Secret bookmark plaintext `title` and `url` columns are `NULL`.
- Secret bookmarks have `items.icon_asset_id = NULL` and may reference encrypted custom icons through `items.secret_icon_asset_id`.
- Secret icon assets are stored in `secret_icon_assets`; the design is documented in `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md`.

Current session behavior:

- Secret bookmarks are hidden after app startup.
- `Cmd+P` on macOS and `Ctrl+P` on Windows/Linux toggles secret visibility.
- If a crypto profile exists but the session is locked, showing secrets opens `UnlockSecretsDialog`.
- After a successful unlock, the runtime data key stays in memory for the current app session.
- Hiding secrets after unlock does not forget the runtime key.
- After one minute without tracked UI activity, visible secret bookmarks are hidden automatically.
- Passive pointer hover does not count as activity; pointer presses/releases, wheel, key input, menu actions, and context actions do.

Projection behavior:

- `MainWindowViewModel` loads the raw storage snapshot.
- `SecretBookmarkProjectionService` filters or decrypts secret bookmarks depending on `SecretSessionService` state.
- Hidden secret bookmarks are not passed to the tree mapper or search index.
- Folders that contain only hidden secret bookmarks are also hidden from the visible tree.
- When secrets are visible, decrypted bookmark payloads are projected into normal visible bookmark records in memory only.
- The search index remains in memory and is rebuilt from the visible projection, so locked secret bookmarks are not searchable.

UI behavior:

- The bookmark editor can create and edit secret bookmarks.
- If the user tries to create a secret bookmark before a master password exists, the app asks to create the master password first.
- The settings window allows changing the master password.
- If a crypto profile exists and the session is locked, changing the master password first asks for the current master password without forcing secrets to become visible.
- Secret bookmarks can use encrypted custom/favicons while unlocked and visible; hidden secret bookmarks are absent from the UI, so their icons are not rendered.

Logging/privacy constraints:

- It is acceptable to log secret subsystem state transitions and failure reasons.
- Do not log master passwords, derived keys, salts, nonces, ciphertext, encrypted payloads, secret generation ids, secret reset event ids, URLs, titles, folder names, search queries, or local icon file paths.

Master-password reset:

- Documented in `Notes/SECRET_RESET_ARCHITECTURE.md`.
- Reset creates a compact `secret_reset_events` marker for the active secret generation.
- Reset physically purges secret bookmark rows and encrypted payloads from live storage.
- Reset also purges folders that only contained secret bookmark content, so their names do not become newly visible after reset.
- Reset deletes the old active crypto profile and marks the runtime secret session as not configured.
- Reset intentionally does not keep full encrypted payload tombstones for every secret bookmark.
- SQLite file compaction is a separate maintenance concern; row deletion frees pages for reuse but may not immediately shrink the database file.

## Search

The standalone search library lives in:

- `Stranichnik.Search/`

The application integration layer lives in:

- `Searching/BookmarkSearchDocumentMapper.cs`
- `Searching/BookmarkSearchService.cs`
- `Searching/BookmarkSearchResultItem.cs`

Current search behavior:

- The main app references `Stranichnik.Search`.
- `BookmarkSearchService` owns an `InMemoryBookmarkSearchIndex`.
- `MainWindowViewModel` loads storage, applies the secret-bookmark projection, rebuilds the search index from that visible projection, and builds the visible tree from the same projection.
- Only visible bookmark records are indexed.
- Folders are not indexed.
- Search index updates happen after successful add/edit/delete bookmark operations.
- Folder deletion currently rebuilds the index from storage so descendant bookmarks do not remain searchable.
- Moving bookmarks or folders does not update search because title/URL search does not depend on folder path yet.
- Search results return IDs and are mapped back to current `BookmarkViewModel` instances before display.

Privacy constraints:

- The index is in memory only.
- Do not persist search index files.
- Do not log search queries, search diagnostics, bookmark titles, URLs, or indexed tokens.
- Locked/hidden secret bookmarks must stay unindexed.
- Visible unlocked secret bookmarks may be indexed in memory only.

## Sample Data

Storage-level sample data lives in:

- `Storage/SampleBookmarkRecordsFactory.cs`

It can be loaded through:

- `Storage/InMemoryBookmarkTreeStore.cs`
- `ViewModels/BookmarkTreeViewModelMapper.cs`

At runtime, sample data is written to SQLite only when the app is launched with `--use-sample-data` and the SQLite database file did not exist before startup.

Legacy direct ViewModel sample data may still exist during cleanup, but it should not be the active loading path.

It intentionally contains edge cases:

- Very long bookmark title and URL.
- Deeply nested folder tree.
- Scroll test folder with many bookmarks.

Keep these stress cases unless the user explicitly asks to remove them. They are useful for checking layout, horizontal scrolling, vertical scrolling, and DnD behavior.

## Drag And Drop

DnD is a custom pointer-based implementation in:

- `Views/MainWindow.axaml.cs`

It does not use native OS drag-and-drop.

Current DnD behavior:

- Drag starts after a small pointer movement threshold.
- A ghost preview follows the cursor.
- The dragged source row is highlighted.
- Non-target rows are dimmed.
- Valid folders show placeholders.
- Drop is accepted only on placeholders.
- Dropping moves the item to the start of the target folder.
- Explicit item ordering is not supported.
- Closed folders can auto-expand during drag after a delay.
- Auto-scroll runs near the left, right, top, and bottom edges of the scroll viewport.
- Layout/scroll compensation is used to reduce visual jumps when placeholders appear or disappear.

Important constraints:

- Do not allow moving a folder into itself.
- Do not allow moving a folder into its own descendant.
- Do not allow moving an item into the same parent.
- Keep move operations centralized so persistence, search, encryption, and sync hooks can stay behind one operation path.

## Storage Boundary

Tree data and mutations are currently routed through:

- `Storage/IBookmarkTreeStore.cs`
- `Storage/InMemoryBookmarkTreeStore.cs`
- `Storage/Sqlite/SqliteBookmarkTreeStore.cs`

Important methods:

- `AddBookmarkToFolderStart(...)`
- `AddFolderToFolderStart(...)`
- `EditBookmark(...)`
- `EditFolder(...)`
- `DeleteItem(...)`
- `CanMoveToFolderStart(...)`
- `MoveToFolderStart(...)`

`MainWindowViewModel` calls this store for initial loading and add/edit/delete/move operations.

The view model still returns operation result records from:

- `ViewModels/BookmarkTreeOperationResults.cs`

These records contain enough information to support UI updates and future undo/search/sync hooks.

Current storage tests cover:

- `InMemoryBookmarkTreeStore`
- `SqliteBookmarkTreeStore`
- SQLite migrations
- `BookmarkTreeViewModelMapper`
- `MainWindowViewModel` storage-path operations

Current SQLite direction:

- Keep SQLite behind `IBookmarkTreeStore`.
- Keep `InMemoryBookmarkTreeStore` for tests and contract comparison.
- Keep Avalonia event handlers free of direct SQLite writes.
- Keep search, encryption, and sync outside view models.
- See `Notes/STORAGE_ARCHITECTURE.md` before changing the storage boundary.

Current UI wiring:

- Add bookmark button opens `BookmarkEditorDialog` and calls `AddBookmarkToFolderStart(...)`.
- Add folder button opens `BookmarkEditorDialog` and calls `AddFolderToFolderStart(...)`.
- Edit bookmark/folder buttons open `BookmarkEditorDialog` and call the corresponding view model operation.
- Delete bookmark/folder buttons open `ConfirmDialog` and call `DeleteItem(...)` after confirmation.
- Open bookmark button launches the URL through the operating system after validation.

Keep future persistence behind the same store/repository boundary. Avoid putting SQLite writes directly in view event handlers.

## Tests

Tests live in:

- `Tests/`

Current test project:

- `Tests/Stranichnik.Tests.csproj`

Current tested area:

- storage records, in-memory store, SQLite store, and migrations
- storage-to-view-model mapper
- `MainWindowViewModel` storage-path operations
- localization

Do not run tests yourself unless the user explicitly asks. Ask the user to run:

```bash
dotnet test Tests/Stranichnik.Tests.csproj
```

## Code Quality

The project uses:

- `.editorconfig`
- built-in .NET analyzers
- `dotnet format --verify-no-changes`

Do not run `dotnet format` yourself. Ask the user to run it and paste the result.

Do not run `dotnet build`, `dotnet run`, or `dotnet test` yourself. Ask the user to run them.

When adding new `.csproj` files, include analyzer settings:

- `AnalysisLevel`
- `AnalysisMode`
- `EnforceCodeStyleInBuild`

## Git And Local Notes

Tracked files under `Notes/` are project memory for agents and should be kept current when architecture or major project state changes.

`AGENTS.md` is also local-only and ignored by git.
