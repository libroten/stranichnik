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

The UI is a tree-like bookmark catalog:

- A synthetic root folder named `Все закладки`.
- Folder rows can be expanded and collapsed.
- Bookmark rows show title and URL.
- A search bar can replace the tree area with bookmark search results.
- Folder and bookmark rows have hover actions.
- Folders have container borders on the left and bottom.
- DnD placeholders appear inside folders while dragging.
- Bookmark URLs can be opened in the system browser.
- Bookmark URLs can also be opened from search results.

The synthetic root folder is special:

- It is visible as a normal folder row.
- It can be expanded and collapsed.
- It cannot be dragged.
- It cannot be edited or deleted.
- It has always-visible add buttons.
- The root drop placeholder is inside this folder, not above the tree.

Dialogs:

- `BookmarkEditorDialog` handles add/edit for both bookmarks and folders.
- `ConfirmDialog` handles delete confirmation for bookmarks and folders.
- `LanguageDialog` handles choosing the application UI language.
- `MessageDialog` handles one-button messages, currently used when opening a page fails.
- Dialogs use `Esc` as cancel/close behavior.

URL opening:

- Implemented in `Views/MainWindow.axaml.cs`.
- Uses `ProcessStartInfo` with `UseShellExecute = true`.
- Only absolute `http` and `https` URLs are accepted.
- Invalid URLs and system launch errors show `MessageDialog` instead of failing silently.

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

Startup logging includes the SQLite database path, whether sample data was requested, and whether migrations/sample seeding ran. Do not log bookmark titles or URLs.

Do not treat the current logger as a complete telemetry system. It is intentionally small and local, useful for diagnostics during development and future sync work.

## UI Styling Direction

The app uses a custom project-owned visual style with light and dark themes. For visually important controls, prefer explicit local styles and custom `ControlTemplate`s over relying on Avalonia's default themed templates.

Avalonia built-in controls may still be used for behavior, layout, focus handling, popups, scrolling, and accessibility. However, hover/pressed/focused backgrounds, borders, foregrounds, spacing, and popup surfaces should be controlled by project styles so the default Avalonia theme palette does not leak into the UI.

Theme support is implemented by Stranichnik, not by Avalonia's built-in light/dark theme switching:

- Do not use `RequestedThemeVariant` as the app theme state.
- Do not use Avalonia `ThemeVariant.Light` or `ThemeVariant.Dark` as the app theme model.
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
- `Stranichnik -> Settings` is currently a placeholder and only closes the menu.
- The appearance dialog currently supports `Light` and `Dark`.
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
- `SearchQuery` and `SearchResults`: current search UI state.

`BookmarkTreeItemViewModel` is the common base for folders and bookmarks. It currently stores:

- `Id`
- `Title`
- `Parent`
- DnD visual state flags.

`BookmarkFolderViewModel` stores:

- `Children`
- `IsExpanded`
- `ExpansionGlyph`
- root/action visibility flags.
- DnD placeholder and hover state flags.

`BookmarkViewModel` stores:

- `Url`

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
- `MainWindowViewModel` loads storage once at startup, rebuilds the search index from that snapshot, and builds the visible tree from the same snapshot.
- Only visible non-secret bookmark records are indexed.
- Folders are not indexed.
- Search index updates happen after successful add/edit/delete bookmark operations.
- Folder deletion currently rebuilds the index from storage so descendant bookmarks do not remain searchable.
- Moving bookmarks or folders does not update search because title/URL search does not depend on folder path yet.
- Search results return IDs and are mapped back to current `BookmarkViewModel` instances before display.

Privacy constraints:

- The index is in memory only.
- Do not persist search index files.
- Do not log search queries, search diagnostics, bookmark titles, URLs, or indexed tokens.
- Secret bookmarks must stay unindexed until future unlock/decryption support exists.

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
- Auto-scroll runs near the top and bottom of the scroll viewport.
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
