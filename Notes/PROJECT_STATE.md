# Stranichnik Project State

This note records the current development state for future agents.

## Current Phase

The project is still in the early in-memory UI phase.

The goal right now is to stabilize the user-facing tree interaction model before adding persistence.

Completed broad areas:

- Avalonia project scaffold.
- `net8.0` target.
- README.
- 0BSD license.
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

Not implemented yet:

- SQLite persistence.
- Real app data loading/saving.
- Search.
- Secret bookmark encryption.
- WebDAV sync.

Implemented in service layer:

- `BookmarkTreeService` supports in-memory add bookmark, add folder, edit bookmark, edit folder, delete item, and move item operations.
- UI buttons are wired to these operations.
- Service result records should remain the boundary for future persistence, search indexing, sync metadata, and secret handling.

Current dialogs:

- `BookmarkEditorDialog` is used for adding/editing bookmarks and folders.
- `ConfirmDialog` is used for delete confirmations.
- `LanguageDialog` is used for selecting the UI language.
- `MessageDialog` is used for one-button error/information messages.
- Dialogs can be closed with `Esc` where that makes sense.

## Current UI Behavior

Main window:

- Shows only the bookmark work area.
- No separate top header.
- Main tree is centered and width-limited.

Root:

- `Все закладки` is displayed as the synthetic root folder.
- It can be opened and closed.
- It has always-visible add bookmark and add folder buttons.
- It has no edit/delete buttons.
- It cannot be dragged.

Folders:

- Have a subtle row background.
- Have a container border on the left and bottom.
- Have rounded container corners.
- Have vertical spacing around the full folder container.
- Can be expanded/collapsed on pointer release.
- Normal folders show action buttons on row hover:
  - add bookmark
  - add folder
  - edit
  - delete

Bookmarks:

- Show title and URL.
- URL is underlined with a dashed underline.
- URL becomes blue on hover.
- Bookmark action buttons show on row hover:
  - open
  - edit
  - delete
- Opening a bookmark accepts only absolute `http` and `https` URLs.
- Invalid or unsupported URLs show an error dialog instead of failing silently.

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
- Auto-scroll works near top/bottom of the scroll viewer.
- Layout compensation is used to prevent large visual jumps when drag starts, folders auto-expand, and drop completes.

Known design choice:

- The app does not support arbitrary manual ordering between siblings.
- New or moved items go to the start of a folder.

## Current Validation State

The user last confirmed that the UI changes work visually.

The assistant must not run:

- `dotnet build`
- `dotnet run`
- `dotnet test`
- `dotnet format`

When verification is needed, ask the user to run commands and paste output.

Useful commands for the user:

```bash
dotnet build
dotnet run
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

## Recommended Next Steps

The next broad implementation area should be persistence.

Suggested order:

1. Read `Notes/FUTURE_FEATURES_PLAN.md`.
2. Read `Notes/DATA_SCHEMA.md`.
3. Introduce repository/storage interfaces without letting Avalonia views mutate persistent state directly.
4. Add SQLite migrations and real app data loading/saving.
5. Keep `BookmarkTreeService` or its successor as the central mutation boundary.
6. After persistence is stable, continue with search, selective encryption, and sync.

SQLite schema direction:

- Use one `items` table for folders and bookmarks.
- Keep the visible root folder synthetic; top-level database rows have `parent_id = NULL`.
- Include metadata for future secret bookmark encryption, in-memory search, tombstone deletes, and item-level WebDAV sync.
- See `Notes/DATA_SCHEMA.md` for the current draft.

For add/edit/delete/move/open, keep model changes centralized. SQLite persistence should be able to observe or wrap service operations instead of duplicating logic in the UI.

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

Important future requirements:

- Selective encryption for secret bookmarks.
- Good bookmark search.
- WebDAV-based sync without a custom backend.

Current future-feature direction:

- Secret bookmarks should use simple application-level payload encryption first. Maximum cryptographic sophistication is not required; hiding plaintext from casual inspection without the password is the main goal.
- Search should initially use an in-memory index, rebuilt from available bookmarks at startup and after unlock. This avoids storing plaintext search terms for secret bookmarks on disk.
- WebDAV sync should be item-level rather than syncing the SQLite file. Some conflict/desync risk remains, but the design should preserve data conservatively with stable IDs, tombstones, and conflict copies.

See `Notes/FUTURE_FEATURES_PLAN.md` before making architectural decisions that affect data model, storage, search, sync, or encryption.

## Important User Preferences

The user is learning Avalonia and wants explanations in plain language.

The user prefers gradual changes, small verified steps, and visual checks after each UI step.

If a proposed design or implementation is questionable, explain the concern and suggest alternatives before implementing.

Do not surprise the user with build/test/run commands. Ask them to run those.
