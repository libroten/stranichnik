# Stranichnik Project State

This note records the current development state for future agents.

## Current Phase

The project has moved beyond the in-memory UI phase into local SQLite persistence, search integration, icon support, selective encryption for secret bookmarks, and first-pass manual WebDAV synchronization.

SQLite-backed bookmark storage is implemented and manually verified. The standalone search library is implemented and integrated into the main app. Selective secret bookmark encryption is implemented. First-pass manual WebDAV sync is implemented in the current branch and is being stabilized through focused automated and manual regression checks.

Completed broad areas:

- Avalonia project scaffold.
- `net8.0` target.
- README.
- 0BSD license.
- `THIRD_PARTY_NOTICES.md` is the user-facing runtime third-party summary;
  `THIRD_PARTY_LICENSES/` stores user-facing runtime license/notice texts for
  binary releases; maintainer-only dependency notes live in
  `Notes/THIRD_PARTY_MAINTENANCE.md` and `Notes/NUGET_LICENSE_METADATA.md`.
  Keep these current when dependencies or external assets change. The in-app
  `Stranichnik -> About` screen displays the project license, third-party
  notices, and bundled license texts from the application output directory, so
  keep that view in sync with release legal files.
- Analyzer/code-style infrastructure.
- xUnit test project.
- In-memory bookmark/folder tree.
- Synthetic root folder `Все закладки`.
- Custom DnD for moving items between folders.
- Basic visual polish for rows, buttons, hover states, ghost, placeholders, and folder containers.
- In-memory add/edit/delete actions for bookmarks and folders.
- Dialogs for bookmark/folder editing, delete confirmation, and simple messages.
- Opening bookmark URLs in the system browser.
- First-pass localization infrastructure with English fallback and Russian translation.
- Language selection dialog reachable from `Service -> Language`.
- Simple last-run file logging with optional stdout duplication.
- UI-independent storage records.
- `IBookmarkTreeStore`.
- `InMemoryBookmarkTreeStore`.
- Storage-to-view-model mapper.
- Main tree add/edit/delete/move operations routed through `IBookmarkTreeStore`.
- SQLite local persistence for bookmark/folder data.
- SQLite migrations.
- Optional first-run sample data seeding through `--use-sample-data`.
- Standalone search architecture is documented in `Notes/SEARCH_ENGINE_ARCHITECTURE.md`.
- Search implementation plan is documented in `Notes/SEARCH_ENGINE_IMPLEMENTATION_PLAN.md`.
- Search application integration guide is documented in `Notes/SEARCH_ENGINE_INTEGRATION_GUIDE.md`.
- Standalone search library first version exists in `Stranichnik.Search/`:
  - public API records and `IBookmarkSearchIndex` are defined;
  - `InMemoryBookmarkSearchIndex` supports validation, rebuild/add/update/remove/clear, exact token search, prefix search, fuzzy typo search, multi-word quality bonuses, and opt-in diagnostics;
  - text normalization, text tokenization, URL tokenization, and BM25-style field-weighted scoring are implemented;
  - average field length statistics are cached for scoring;
  - prefix expansion uses a sorted in-memory term set instead of scanning the full postings dictionary;
  - `Rebuild` is failure-atomic and duplicate IDs use last-document-wins semantics;
  - `AddOrUpdate` prepares the new indexed document before replacing the old one;
  - library usage and privacy notes are documented in `Stranichnik.Search/README.md`;
  - independent `Stranichnik.Search.Tests/` test project covers normalization, tokenization, URL parsing, exact/prefix/fuzzy search, ranking, diagnostics, mutation behavior, and edge cases.
- Search is integrated into the main app:
  - the main app references `Stranichnik.Search`;
  - application-side search integration lives in `Searching/`;
  - `MainWindowViewModel` rebuilds the in-memory index from the same storage snapshot used to build the tree;
  - add/edit/delete bookmark operations update the search index;
  - folder delete rebuilds the search index from storage;
  - the main window has a search bar and a search-results view that replaces the tree while search is active;
  - search result rows have an open action.
- Project-owned light/dark theme support is implemented:
  - theme state is stored in `settings.json`;
  - theme logic lives in `Theming/`;
  - the app does not use Avalonia theme variants as the theme model;
  - `Service -> Appearance` opens an appearance dialog with System/Light/Dark choices;
  - main window and dialogs use semantic theme resources for visible colors.
- Bookmark title metadata fetching is implemented:
  - `BookmarkEditorDialog` fetches page metadata for bookmark URLs after a debounce;
  - the discovered page title is shown as a clickable suggestion and is not forced into the title field automatically;
  - favicon candidates are discovered while fetching metadata;
  - a bounded number of favicon candidates is downloaded and processed in memory;
  - large HTML responses use a partial fallback that can still find a complete title tag and favicon link tags in the downloaded prefix;
  - network requests are cancellable and stale responses are ignored;
  - HTML parsing uses AngleSharp;
  - logs record fetch states and fallback outcomes without logging URLs or discovered titles.
- Bookmark and folder icon support is implemented:
  - main tree rows and search result rows show default or custom icons;
  - action buttons, folder expansion chevrons, search clear, and editor icon-choice placeholders use project vector resources instead of text/Unicode glyphs;
  - SQLite stores immutable custom/favicon icon assets in `icon_assets`;
  - items reference icon assets through nullable `items.icon_asset_id`;
  - default icons remain app resources/vector UI and are not stored in SQLite;
  - icon assets are deduplicated by SHA-256 of original source bytes;
  - selected/uploaded/favicon images are processed to `64x64` PNG before storage;
  - `BookmarkEditorDialog` lets the user choose current, default, discovered favicon, or uploaded icon;
  - uploaded icon files are selected through Avalonia's cross-platform storage provider;
  - downloaded/uploaded icon data is persisted only when the editor dialog is saved;
  - icon-related logs do not include URLs, page titles, local file paths, or raw hashes.
- Encrypted custom/favicons for secret bookmarks are implemented:
  - secret icon assets are stored in SQLite table `secret_icon_assets`;
  - secret bookmarks reference them through nullable `items.secret_icon_asset_id`;
  - processed secret icon PNG bytes are encrypted with the runtime secret DEK;
  - secret icon assets deduplicate by original source SHA-256 inside `secret_icon_assets`;
  - normal-to-secret and secret-to-normal bookmark conversion preserve icons by copying between regular and secret icon tables;
  - decrypted secret icon bitmaps are cached only in memory and the cache is cleared when the secret session changes;
  - master-password reset physically purges secret icon assets for the reset generation.
- The icon library is implemented:
  - bookmark/folder editor dialogs can open a library of already stored icons;
  - the library shows only icons referenced by visible items, not orphan icon assets;
  - visible entries are deduplicated by source hash across regular and secret icon tables;
  - hidden secret icon assets do not appear while secrets are hidden or locked;
  - when secrets are visible, secret icons can be selected and converted to regular storage for normal bookmarks/folders;
  - regular icons can be converted to encrypted secret storage when selected for secret bookmarks;
  - bookmark icon suggestions are sorted by host-label similarity with the URL typed in the editor;
  - folder icon suggestions put folder-used icons before bookmark-only icons.
- Bookmark tree context menus are implemented:
  - right-clicking a bookmark opens a custom context menu with go, copy URL, edit, and delete actions;
  - right-clicking a normal folder opens a custom context menu with add bookmark, add folder, edit, and delete actions;
  - right-clicking the synthetic root folder opens a custom context menu with only add bookmark and add folder actions;
  - bookmark URL copying uses Avalonia's cross-platform clipboard abstraction;
  - popup menus use project-owned styling and custom shadows instead of relying on platform-specific popup shadows.
- Selective secret bookmark encryption is implemented:
  - secret bookmark title and URL are encrypted in SQLite;
  - secret bookmark plaintext title and URL columns are kept `NULL`;
  - a SQLite crypto profile stores KDF metadata, wrapped data key material, and password-check payload;
  - the crypto model uses PBKDF2-SHA256 plus AES-256-GCM with a DEK/KEK split;
  - changing the master password rewraps the data key instead of re-encrypting all bookmark payloads;
  - secrets are hidden after startup;
  - `Cmd+P` on macOS and `Ctrl+P` on Windows/Linux toggles secret visibility;
  - `Service -> Show secrets` / `Service -> Показывать секреты` provides the same toggle through the top menu with an ON/OFF indicator;
  - the master password is required once per app session to unlock secrets;
  - after unlock, hiding secrets keeps the runtime data key in memory for the session;
  - visible secrets auto-hide after one minute without tracked UI activity;
  - bookmark/folder editor dialogs pause auto-hide while open and restart the inactivity timer after closing;
  - folders containing only hidden secret bookmarks are hidden too;
  - hidden secret bookmarks are absent from the tree and in-memory search index;
  - visible unlocked secret bookmarks are projected in memory and may be searched during that unlocked-visible state;
  - secret bookmarks can use encrypted custom/favicon icons while visible;
  - encrypted custom icon design is documented in `Notes/ENCRYPTED_SECRET_ICONS_ARCHITECTURE.md`.
- Secret master-password reset is implemented:
  - reset uses `secret_generation_id` plus compact `secret_reset_events`;
  - reset physically purges secret bookmark rows and encrypted payloads from live storage;
  - reset also purges folders that only contained secret bookmark content;
  - reset deletes the old active crypto profile;
  - reset does not keep full encrypted payload tombstones for every secret bookmark;
  - reset is exposed from the settings window with destructive confirmations;
  - details are documented in `Notes/SECRET_RESET_ARCHITECTURE.md`.
- Secret bookmark settings UI is implemented:
  - `Stranichnik -> Settings` opens a scrollable settings dialog;
  - the first section is `Secret bookmarks`;
  - the section supports setting/changing the master password;
  - changing the master password from a locked configured state asks for the current password first without forcing secrets to become visible.
- First-pass manual WebDAV sync is implemented:
  - sync architecture and implementation plan are documented in `Notes/WEBDAV_SYNC_ARCHITECTURE.md` and `Notes/WEBDAV_SYNC_IMPLEMENTATION_PLAN.md`;
  - the app syncs logical JSON objects through WebDAV instead of uploading the SQLite database file;
  - the remote repository uses `manifest.json` plus category directories for items, regular icon assets, encrypted secret icon assets, crypto profiles, secret reset events, and device metadata;
  - sync supports pull, push, repository initialization, remote object validation, canonical content hashing, quarantine of invalid remote objects, pending icon references, deferred secret items, and reset-generation filtering;
  - unchanged already-quarantined remote objects are treated as known problems rather than fresh sync failures, while changed invalid objects are surfaced again;
  - local SQLite rows carry sync metadata such as state, remote ETag, last synced timestamp, content hash, and modified device ID;
  - existing local objects are marked dirty during sync metadata migration so a first sync uploads the whole local dataset instead of only post-migration edits;
  - sync settings live in `settings.json`, but raw WebDAV passwords are kept behind `ISyncCredentialStore`;
  - WebDAV passwords can be remembered between app sessions through the OS credential store where available;
  - if the OS credential store is unavailable, the user must explicitly confirm an obfuscated local fallback file, and the settings window shows a persistent red warning banner while that fallback is active;
  - `--simulate-unavailable-system-credential-store` forces the OS credential backend to be unavailable for manual fallback testing;
  - `Stranichnik -> Settings -> Sync` exposes WebDAV URL, username, password, connection test, sync now, sync-settings reset, last successful sync status, and quarantined remote problem management;
  - after sync pulls a crypto profile into an initially empty local database, `MainWindowViewModel` refreshes the runtime secret-session configuration so `Cmd+P` can unlock the downloaded secret bookmarks without restarting the app;
  - logs report non-sensitive sync summaries and errors without logging WebDAV credentials, bookmark URLs/titles, source hashes, payloads, or secret generation IDs.

Not implemented yet:

- automatic/background sync;
- advanced user-facing sync conflict resolution UI;
- remote garbage collection for orphaned old sync objects;
- full cross-platform release packaging polish.

Implemented in storage/view-model layer:

- `InMemoryBookmarkTreeStore` supports in-memory add bookmark, add folder, edit bookmark, edit folder, delete item, can-move checks, and move item operations.
- `SqliteBookmarkTreeStore` supports SQLite add bookmark, add folder, edit bookmark, edit folder, delete item, can-move checks, and move item operations.
- `App.axaml.cs` creates the runtime `SqliteBookmarkTreeStore`, creates the search service, and injects both into `MainWindowViewModel`.
- `MainWindowViewModel` loads runtime data through `IBookmarkTreeStore -> BookmarkTreeViewModelMapper`.
- `MainWindowViewModel` also rebuilds search from the initial storage snapshot and keeps search in sync with successful bookmark mutations.
- New SQLite databases start empty by default.
- `--use-sample-data` seeds sample data only when the SQLite file did not exist before startup.
- Tests can inject `InMemoryBookmarkTreeStore` into `MainWindowViewModel`.
- UI buttons and DnD are wired to view model operations that update the store first and then update the visible tree.
- Operation result records live in `ViewModels/BookmarkTreeOperationResults.cs`.
- The old `BookmarkTreeService` has been removed.

Current dialogs:

- `BookmarkEditorDialog` is used for adding/editing bookmarks and folders.
- For bookmark add/edit, it can suggest a fetched page title below the title field.
- For bookmark add/edit, it can also offer a discovered favicon as a pending icon choice.
- For bookmark and folder add/edit, it can reset to the default icon, select a local image file, or choose an already stored visible icon from the icon library.
- `ConfirmDialog` is used for delete confirmations.
- `LanguageDialog` is used for selecting the UI language.
- `MessageDialog` is used for one-button error/information messages.
- `AppearanceDialog` is used for selecting the application theme.
- `SetMasterPasswordDialog` is used for first-time secret master password setup.
- `UnlockSecretsDialog` is used for unlocking secret bookmarks.
- `SettingsDialog` is used for app settings and currently contains the secret bookmark password section and WebDAV sync section.
- Dialogs can be closed with `Esc` where that makes sense.
- Inline form errors, success states, and neutral in-progress states use `Views/StatusBanner.axaml`.
- `StatusBanner` should be reused for future dialog-local validation/status messages instead of adding raw error `TextBlock`s.
- WebDAV-backed sync operations report active/inactive state through `SyncActivityService`.
  Full sync runs also report final success/failure. The main window shows a bottom
  activity bar while sync is active, briefly holds a full green/red completion bar after
  sync finishes, and the settings window shows an animated in-progress status message.

## Current UI Behavior

Main window:

- Shows only the bookmark work area.
- No separate top header.
- Main tree is centered and width-limited.
- Search bar is displayed above the work area.
- When search is active, search results replace the tree in the same work area.
- When search is inactive, the normal tree is shown.
- Uses semantic theme resources so light/dark colors can switch at runtime.

Theme:

- Light is the default for missing or invalid settings.
- System, Light, and Dark can be selected from `Service -> Appearance`.
- System mode reads the OS theme signal and maps it to the project-owned light
  or dark palette.
- Theme changes apply immediately and are persisted to `settings.json`.
- The implementation is project-owned and does not use Avalonia `ThemeVariant` as the model.

Root:

- `Все закладки` is displayed as the synthetic root folder.
- It can be opened and closed.
- It has always-visible add bookmark and add folder buttons.
- It has no edit/delete buttons.
- It cannot be dragged.
- Its context menu contains only add bookmark and add folder actions.

Folders:

- Have a subtle row background.
- Have a container border on the left and bottom.
- Have rounded container corners.
- Have vertical spacing around the full folder container.
- Can be expanded/collapsed on pointer release.
- When the tree is first created without a previously supplied expanded-folder
  state, the first two real folder levels below the synthetic root are expanded
  by default. The synthetic root is not counted as a real folder level.
- Normal folders show action buttons on row hover:
  - add bookmark
  - add folder
  - edit
  - delete
- Normal folders also have a right-click context menu with the same actions.

Bookmarks:

- Show a default or custom icon.
- Show title and URL.
- Can be marked as secret in the bookmark editor.
- Secret bookmarks are hidden while secrets are locked/hidden.
- Secret bookmarks can use encrypted custom/favicon icons while secrets are unlocked and visible.
- URL is underlined with a dashed underline.
- URL becomes blue on hover.
- While adding/editing a bookmark, the app can fetch the page title from the URL and show it as an italic dashed-underlined suggestion.
- While adding/editing a bookmark, the app can fetch and offer a favicon as an icon option.
- While adding/editing a bookmark, the app can choose an existing visible stored icon from the icon library.
- Bookmark action buttons show on row hover:
  - open
  - edit
  - delete
- Bookmark right-click context menus provide:
  - go/open
  - copy URL to clipboard
  - edit
  - delete
- Opening a bookmark accepts only absolute `http` and `https` URLs.
- Invalid or unsupported URLs show an error dialog instead of failing silently.

Search:

- Search is better than substring matching because it uses the standalone in-memory search library.
- Search results are separate from the tree; the tree is not filtered or mutated for search display.
- Search result rows show bookmark title and URL.
- Search result rows show the same custom/default icon as the corresponding bookmark.
- Search result rows have an open action.
- URL hover styling in search results matches normal bookmark URL hover styling.
- The clear button empties the query and returns to the tree.

Action buttons:

- Have transparent background by default.
- Have always-visible colored border and icon.
- Hover background is a very light version of the action color.
- Colors:
  - open: blue
  - add bookmark: green
  - add folder: blue
  - edit: yellow
  - delete: red

Menus:

- The top menu row currently contains:
  - `Stranichnik`
  - `Service`
- `Service -> Appearance` opens the appearance selector.
- `Service -> Language` opens the language selector.
- `Service -> Show secrets` toggles secret visibility and displays an ON/OFF state indicator.
- `Stranichnik -> Settings` opens the settings window.
- `Stranichnik -> About` opens the about/license window.
- `Stranichnik -> Exit` closes the application.
- Top popup menus and tree context menus share custom menu styling.
- Menu shadows are drawn inside transparent padded popup hosts. Clicking the padded shadow area closes the popup.

Secret bookmarks:

- `Cmd+P` on macOS and `Ctrl+P` on Windows/Linux toggles secret visibility.
- If secrets are configured but locked, the toggle opens the unlock dialog.
- If secrets are visible and the user is inactive for one minute, the app hides secrets automatically.
- Inactivity currently tracks pointer presses/releases, wheel, keyboard input, menu actions, and context actions.
- Passive pointer hover does not reset the inactivity timer.
- Secret bookmarks are not shown after app startup until explicitly unlocked/shown.

Hover transitions:

- Shared transition duration is defined as `HoverTransitionDuration` in `Views/MainWindow.axaml`.
- It is used for row background transitions, placeholder transitions, and action button fade in/out.

## Current DnD Behavior

DnD is custom and pointer-driven.

Current behavior:

- Drag begins after pointer movement threshold.
- Ghost preview appears near the cursor.
- Source row is highlighted.
- Non-target rows are dimmed.
- Placeholders appear inside open folders.
- Drop works only on placeholders.
- Drop moves dragged item to the start of the target folder.
- Root drop target is the placeholder inside `Все закладки`.
- Closed folders can auto-expand while dragging.
- Auto-scroll works near the left, right, top, and bottom edges of the scroll viewer.
- Layout compensation is used to prevent large visual jumps when drag starts, folders auto-expand, and drop completes.

Known design choice:

- The app does not support arbitrary manual ordering between siblings.
- New or moved items go to the start of a folder.

## Current Validation State

The user last confirmed successful build, tests, formatting verification, and manual WebDAV regression checks for the current sync branch.

Recently verified sync scenarios include:

- pushing local data to an empty WebDAV folder;
- deleting the local SQLite database and pulling data back into a fresh empty database;
- ensuring existing sample-data rows are uploaded during first sync after sync metadata migration;
- pulling secret bookmarks, crypto profile data, and encrypted secret icons into an empty database;
- unlocking downloaded secret bookmarks with `Cmd+P` after pull without restarting the app.

The user confirmed the first search integration works in the app. The current search quality is acceptable as a first pass, with possible future tuning.

The user also confirmed successful standalone search library verification after the latest review fixes:

```bash
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
```

The assistant must not run:

- `dotnet build`
- `dotnet run`
- `dotnet test`
- `dotnet format`

When verification is needed, ask the user to run commands and paste output.

Useful commands for the user:

```bash
dotnet build
dotnet run -- --print-logs-to-console
dotnet test Tests/Stranichnik.Tests.csproj
dotnet build Stranichnik.Search/Stranichnik.Search.csproj
dotnet test Stranichnik.Search.Tests/Stranichnik.Search.Tests.csproj
dotnet format --verify-no-changes
```

## Recommended Next Steps

The next broad area is likely stabilization and manual regression of the first-pass WebDAV sync implementation, unless the user chooses to pause sync and polish search, encryption, icons, or UI first.

Likely order:

1. Continue manual WebDAV sync regression on macOS, then Windows and Linux.
2. Review sync conflict handling and any remaining edge cases found during two-device testing.
3. Add automatic/background sync only after manual sync is stable.
4. Revisit search quality tuning if the user wants better ranking/tokenization.

SQLite schema direction:

- Use one `items` table for folders and bookmarks.
- Keep the visible root folder synthetic; top-level database rows have `parent_id = NULL`.
- Include metadata for selective secret bookmark encryption, in-memory search rebuilds, tombstone deletes, and item-level WebDAV sync.
- See `Notes/DATA_SCHEMA.md` for the current draft.

Storage architecture direction:

- Keep UI-independent storage/domain records.
- Keep `IBookmarkTreeStore` as the boundary.
- Use `SqliteBookmarkTreeStore` at runtime.
- Keep `InMemoryBookmarkTreeStore` for tests/reference.
- Keep search library internals, encryption, and sync outside Avalonia view models.
- Keep application search integration behind `Searching/BookmarkSearchService`.
- See `Notes/STORAGE_ARCHITECTURE.md` for the current design.

For add/edit/delete/move/open, keep model changes centralized. Do not duplicate storage mutations in UI event handlers.

Localization direction:

- UI strings should come from `.resx` resources.
- English is the default/fallback language.
- Russian is provided as a localized resource file.
- Language selection is stored in local app settings and currently requires app restart for a full UI refresh.
- Do not localize user data such as folder names, bookmark titles, or URLs.
- See `Notes/ARCHITECTURE.md` for the localization design notes.

Logging direction:

- Use `Diagnostics/Logs.Print(...)` for simple local diagnostics.
- The log file is stored in the app data directory as `stranichnik.log`.
- The log file is overwritten on each application start and contains only the latest run.
- `--print-logs-to-console` duplicates log lines to stdout.
- The app logs the app data directory path during startup.
- Bookmark metadata fetching logs diagnostic states and fallback outcomes, but must not log bookmark URLs or discovered page titles.

Important future requirements:

- Selective encryption for secret bookmarks.
- Good bookmark search.
- WebDAV-based sync without a custom backend.

Current future-feature direction:

- Secret bookmarks use application-level payload encryption. Maximum cryptographic sophistication is not the goal, but the current implementation still uses a standard DEK/KEK split so password changes are cheap.
- Search uses an in-memory index rebuilt from the currently visible bookmark projection. This avoids storing plaintext search terms for hidden secret bookmarks on disk.
- WebDAV sync should be item-level rather than syncing the SQLite file. Some conflict/desync risk remains, but the design should preserve data conservatively with stable IDs, tombstones, and conflict copies.

See `Notes/FUTURE_FEATURES_PLAN.md` before making architectural decisions that affect data model, storage, search, sync, or encryption.

## Important User Preferences

The user is learning Avalonia and wants explanations in plain language.

The user prefers gradual changes, small verified steps, and visual checks after each UI step.

If a proposed design or implementation is questionable, explain the concern and suggest alternatives before implementing.

Do not surprise the user with build/test/run commands. Ask them to run those.
