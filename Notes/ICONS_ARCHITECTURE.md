# Stranichnik Item Icons Architecture

This note describes the architecture for bookmark and folder icons.

Important: this is not a fixed contract. It records the current preferred design so future agents can continue without reconstructing chat context. If implementation reveals a simpler or safer approach, update this note and the current plan.

## User Goal

Every bookmark and folder should have an icon.

The icon can come from:

- the built-in default icon for the item type;
- a website favicon discovered while fetching bookmark page metadata;
- an image file chosen by the user from disk.

The user should be able to choose or change the icon while adding/editing a bookmark or folder.

## Current Implementation Snapshot

Default icon resources have been added under:

```text
Assets/Icons/bookmark-default-light.svg
Assets/Icons/bookmark-default-dark.svg
Assets/Icons/folder-default-light.svg
Assets/Icons/folder-default-dark.svg
```

They are based on the reference SVG files in `icons/` and are recolored to match the current app accent colors:

- light theme accent: `#35416D`;
- dark theme accent: `#A39B88`.

Because `Stranichnik.csproj` already includes `Assets/**` as `AvaloniaResource`, these files are application resources.

The currently visible default icons in the main tree and editor dialog are rendered with inline XAML vector paths that use theme resources. The SVG files remain useful as source assets/project resources, but the UI does not depend on rasterized default icons.

## Visual Requirements

Main tree rows:

- folder rows display a folder icon;
- bookmark rows display a bookmark icon or custom/favicon icon;
- if no custom icon is assigned, show the themed default icon for the item kind;
- if an assigned icon cannot be loaded, fall back to the themed default icon;
- icons should not disturb existing row alignment.

Recommended row display size:

- `20x20` or `24x24` logical pixels in the main tree;
- start with `20x20` if the row feels dense;
- use a fixed icon slot so row text does not shift when icons load.

Stored custom icon size:

- process user/favicons into `64x64` PNG;
- display can scale down from this;
- `64x64` is large enough for HiDPI row rendering and small enough to keep SQLite blobs modest.

Dialog UI:

- `BookmarkEditorDialog` shows icon choices while adding/editing items.
- Options include:
  - current icon, when editing an item that already has a custom/favicon icon;
  - discovered favicon, when available;
  - default icon for this item kind;
  - a button to choose a local image file.
- Selecting an option should update the pending dialog state.
- The icon change is persisted only when the user saves the dialog.
- Cancelling the dialog must not store a newly fetched or uploaded image.

## Storage Direction

Use SQLite for custom/favicon icons.

Default icons:

- are not stored in SQLite;
- live as app resources in `Assets/Icons`;
- are selected by item kind and current app theme when `items.icon_asset_id` is `NULL`.

Custom/favicon icons:

- are stored as processed image blobs in SQLite;
- are immutable assets;
- items reference icon assets by id.

Important rule:

- Never mutate an existing icon blob to update one item. Multiple items may reference the same icon asset. Changing an item icon must change that item's `icon_asset_id`, not the shared blob.

## SQLite Schema

Add a table for processed icon assets:

```sql
CREATE TABLE icon_assets (
    id TEXT PRIMARY KEY,

    source_hash_algorithm TEXT NOT NULL,
    source_hash TEXT NOT NULL,
    source_size_bytes INTEGER NOT NULL,

    processed_mime_type TEXT NOT NULL,
    processed_width INTEGER NOT NULL,
    processed_height INTEGER NOT NULL,
    processed_bytes BLOB NOT NULL,

    created_at_utc TEXT NOT NULL,

    UNIQUE (source_hash_algorithm, source_hash)
);
```

Add an optional reference from `items`:

```sql
ALTER TABLE items
    ADD COLUMN icon_asset_id TEXT NULL REFERENCES icon_assets(id);
```

Notes:

- `source_hash` is computed from the original bytes before processing.
- Use SHA-256 for `source_hash` and store it as lowercase hex.
- `processed_mime_type` should initially be `image/png`.
- `processed_width` and `processed_height` should initially be `64`.
- `items.icon_asset_id = NULL` means use the default themed icon.
- The database may contain unreferenced icon assets after edits/deletes; cleanup can be a later maintenance task.

## Deduplication Algorithm

When a favicon is downloaded or a user picks a file:

1. Read the original bytes.
2. Enforce an input byte-size limit.
3. Compute SHA-256 from the original bytes.
4. Check `icon_assets` for `(source_hash_algorithm = 'sha256', source_hash = hash)`.
5. If found, reuse the existing `icon_assets.id`.
6. If not found and the user eventually saves the item with this icon:
   - decode the original image;
   - convert it to a square `64x64` PNG;
   - insert a new immutable row into `icon_assets`;
   - assign the new `icon_asset_id` to the item.

This avoids duplicate blobs when the same source icon is used by many bookmarks/folders.

This does not deduplicate visually identical images encoded differently. That is acceptable for the first implementation.

## Image Processing

The image processing boundary lives under:

```text
IconProcessing/
```

Responsibility:

- validate that input bytes decode as an image;
- normalize orientation if the chosen decoder exposes it;
- resize/crop/letterbox to a square `64x64`;
- encode PNG bytes;
- return a processed icon model.

Current implementation:

- icon-processing code lives in `IconProcessing/` with namespace `Stranichnik.Icons`;
- `IconAssetService` computes SHA-256 from original bytes, checks storage deduplication, then creates immutable icon assets;
- `IconImageProcessor` uses Avalonia bitmap/render APIs to decode, fit into a transparent square, and save PNG;
- `IconProcessingOptions` currently uses `64x64` output PNG icons and a `1 MiB` max input byte limit for user-selected source files;
- `BookmarkIconImageCache` lazily decodes custom PNG blobs and falls back to default icons when needed;
- no additional image-processing NuGet package has been added yet;
- unit tests cover hashing/deduplication/service behavior with a fake image processor, so tests do not depend on Avalonia graphics backend initialization.

Recommended first output:

- PNG;
- `64x64`;
- transparent background when the source supports alpha;
- preserve aspect ratio by fitting inside the square with transparent padding, not by distortion.

Implementation options:

- Prefer a cross-platform managed image library if the dependency/license is acceptable.
- If avoiding another dependency, evaluate Avalonia/Skia bitmap APIs, but keep them behind `IconImageProcessor` so the implementation can be replaced.

Do not scatter image decode/resize logic through dialogs, view models, or storage classes.

## Favicon Discovery

Extend bookmark metadata extraction so it can also find favicon candidates.

Current model shape:

```csharp
public sealed record BookmarkPageMetadata(
    string? Title,
    IReadOnlyList<BookmarkIconCandidate> IconCandidates,
    BookmarkFetchedIcon? Favicon);

public sealed record BookmarkIconCandidate(
    Uri Uri,
    string? Rel,
    string? Type,
    string? Sizes);

public sealed record BookmarkFetchedIcon(
    BookmarkIconCandidate Candidate,
    ReadOnlyMemory<byte> OriginalBytes,
    ProcessedIconImage Image);
```

Candidate sources:

1. `<link rel="icon" ...>`
2. `<link rel="shortcut icon" ...>`
3. `<link rel="apple-touch-icon" ...>`
4. `<link rel="apple-touch-icon-precomposed" ...>`
5. optional later: `<link rel="mask-icon" ...>`
6. fallback candidate: `/favicon.ico` relative to the page origin.

Candidate handling:

- resolve relative `href` values against the final page URI;
- ignore missing or invalid `href`;
- prefer candidates whose declared size is closest to `64x64`;
- prefer raster formats that the image processor can decode;
- if no declared size exists, try candidates in document order with a modest cap;
- do not fetch every possible icon candidate without limits.

Favicon download rules:

- use the same no-backend principle as page title fetching;
- make direct desktop app HTTP requests;
- do not send credentials/cookies;
- enforce timeout and cancellation;
- enforce max downloaded icon bytes; the current limit is `256 KiB`;
- try at most a small bounded number of candidates; the current cap is 4;
- do not log favicon URLs;
- do not persist a downloaded favicon until the user chooses it and saves the dialog.

Large HTML fallback:

- normal metadata parsing reads only a bounded prefix of the HTML response;
- if the response exceeds the current `512 KiB` HTML limit, the app tries fallback parsing against the downloaded prefix;
- fallback parsing looks for complete `<title>...</title>` and supported `<link ...>` favicon tags in the prefix;
- fallback parsing also adds `/favicon.ico`;
- this cannot discover metadata that appears after the downloaded prefix, which is an accepted first-version tradeoff.

If favicon download or processing fails:

- do not show the favicon option in the UI;
- keep the default/custom options available.

## Local File Picker

Use Avalonia's cross-platform storage provider API:

```text
TopLevel.GetTopLevel(this)?.StorageProvider.OpenFilePickerAsync(...)
```

or the equivalent `Window.StorageProvider` access available in the current Avalonia version.

Requirements:

- works on macOS, Windows, and Linux;
- filter to image files where supported;
- still validate the selected file by decoding it;
- enforce max input size before processing;
- if the selected file cannot be decoded, show a friendly error dialog;
- do not store the selected file until the item is saved.

The older platform-specific file dialog APIs should be avoided unless the current Avalonia version requires them.

Current implementation:

- `BookmarkEditorDialog` uses `Window.StorageProvider.OpenFilePickerAsync`;
- picker filters common image file extensions;
- the selected file is read, size-checked, processed, previewed, and kept only as pending dialog state;
- no local file path is written to logs.

## View Model And Store Changes

Storage records:

- `BookmarkItemRecord` has `IconAssetId`;
- `BookmarkIconAssetRecord` represents stored icon assets;
- keep default icons as `IconAssetId = null`.

Store interface:

- item records include optional `IconAssetId`;
- storage exposes icon asset lookup/reuse/create operations;
- item icon assignment is persisted through `SetItemIconAsset`;
- keep SQLite details out of Avalonia dialogs.

Possible future application boundary:

```text
BookmarkEditorDialog
  -> MainWindow.axaml.cs
    -> MainWindowViewModel / future application service
      -> icon service + IBookmarkTreeStore
```

The dialog should return a pending icon selection, not write to SQLite directly.

Current application flow:

1. The dialog collects a pending `BookmarkIconSelection`.
2. `MainWindowViewModel` saves or reuses an icon asset through `IconAssetService`.
3. The item record is created/edited/moved through `IBookmarkTreeStore`.
4. The item's `IconAssetId` is updated when a custom/favicon icon is selected.
5. The view model is reloaded from storage so the UI reflects the persisted icon state.

## UI Icon Loading

An in-memory icon cache avoids decoding blobs repeatedly for every row:

- key: `icon_asset_id` for custom/favicon icons;
- key: item kind + theme for default icons;
- value: decoded Avalonia image/bitmap object suitable for binding.

Cache behavior:

- render themed default vector icons through the UI when no custom icon is assigned;
- load custom PNG blobs lazily;
- missing/corrupt blobs fall back to default icons;
- when theme changes, default icon bindings should update to the themed default;
- custom icons do not change with theme.

Known cleanup opportunity:

- default icon vector paths are currently duplicated between main tree/search UI and editor preview UI;
- a future small `IconPresenter` control or reusable style could reduce duplication without changing behavior.

## Search Interaction

Search should ignore icon blobs.

Icons must not affect search indexing or ranking.

## Encryption Interaction

Icons can leak information.

Examples:

- a favicon may reveal the site/domain of a secret bookmark;
- a user-selected custom icon may reveal private meaning even if the bookmark title/url are encrypted.

First implementation direction:

- non-secret bookmarks/folders may use custom/favicon icon assets stored in plaintext SQLite blobs;
- secret bookmarks currently use default icons only;
- encrypted custom icons for secret bookmarks are deferred to `Notes/ENCRYPTED_SECRET_ICONS_DRAFT.md`;
- while secrets are locked, secret bookmarks are hidden anyway;
- do not auto-store favicons for secret bookmarks later without an explicit privacy decision.

Possible later options for secret bookmarks:

- force default icons while locked and keep custom icon data inside encrypted payload;
- allow non-secret-looking custom icons but document the privacy tradeoff;
- store encrypted icon blobs for secret items.

## Sync Interaction

WebDAV sync should not sync the SQLite file directly.

For future item-level sync:

- `items.icon_asset_id` becomes part of the item sync payload;
- icon assets can be synced as separate objects keyed by stable `id` or by content hash;
- because icon assets are immutable, conflict handling is simpler;
- if two devices add the same source icon, SHA-256 deduplication can avoid duplicate logical assets during merge.

## Logging

Do not log:

- local file paths chosen by the user;
- favicon URLs;
- bookmark URLs;
- discovered titles;
- raw hashes if they are not needed for diagnostics.

Safe logs:

- icon candidate discovery succeeded/failed;
- favicon download succeeded/failed;
- icon processing succeeded/failed;
- icon asset reused/created, without source path or URL.

## Non-Goals For First Implementation

- Built-in icon gallery beyond default bookmark/folder icons.
- User-editable icon crop UI.
- Arbitrary per-theme custom icons.
- Persistent disk thumbnail cache outside SQLite.
- Automatic favicon refresh for existing bookmarks.
- Sync implementation.
- Secret/encrypted custom icon storage.
