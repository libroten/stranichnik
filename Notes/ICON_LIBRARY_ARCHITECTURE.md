# Icon Library Architecture

This document describes the planned icon-library feature for Stranichnik.

It is implementation guidance for future agents, not an irreversible contract.
If implementation uncovers a simpler or safer approach, update this document and
the current plan before changing direction.

## Goal

When adding or editing a bookmark or folder, the user should be able to choose
an icon that already exists in the local database.

Existing icon sources remain available:

- current icon;
- default item icon;
- fetched favicon;
- uploaded image file.

The icon library adds another source:

- an already stored icon asset that is currently used by at least one visible
  bookmark or folder.

## Current Implementation Snapshot

Version one is implemented in the application.

Main files:

- `IconProcessing/IconLibraryModels.cs`;
- `IconProcessing/IconLibraryHostScore.cs`;
- `IconProcessing/IconLibraryService.cs`;
- `Views/IconLibraryDialog.axaml`;
- `Views/IconLibraryDialog.axaml.cs`;
- `Views/BookmarkEditorDialog.axaml`;
- `Views/BookmarkEditorDialog.axaml.cs`;
- `ViewModels/MainWindowViewModel.cs`.

The implemented version follows the rules in this document:

- shows only referenced icons from the current visible projected snapshot;
- excludes secret icon assets while secrets are hidden or locked;
- deduplicates displayed entries by source hash across regular/secret tables;
- ranks bookmark icon choices by host-label similarity;
- ranks folder icon choices by folder usage first;
- applies the selected icon through `MainWindowViewModel`, converting between
  regular and secret icon storage when the final target state requires it.

## Current Icon Storage Baseline

Regular icons:

- stored in `icon_assets`;
- referenced by `items.icon_asset_id`;
- used by folders and non-secret bookmarks;
- processed PNG bytes are plaintext SQLite blobs;
- deduplicated by SHA-256 of original source bytes inside `icon_assets`.

Secret icons:

- stored in `secret_icon_assets`;
- referenced by `items.secret_icon_asset_id`;
- used only by secret bookmarks;
- processed PNG bytes are encrypted;
- source hashes are intentionally stored in plaintext for deduplication;
- deduplicated by SHA-256 of original source bytes inside
  `secret_icon_assets`;
- decrypted bitmap cache is memory-only and cleared when the secret session
  changes.

The current implementation already supports copying/converting icons between
regular and secret storage when a bookmark changes secret state:

- regular icon -> encrypted secret icon;
- encrypted secret icon -> regular icon.

The icon library should reuse this direction instead of inventing a third icon
storage category.

## Visibility Rules

If secrets are hidden or locked:

- do not show any `secret_icon_assets` in the library;
- do not use secret bookmarks or secret bookmark URLs for icon ranking;
- do not decrypt secret icon blobs;
- do not show secret icon previews.

If secrets are currently visible:

- include secret icon assets used by visible secret bookmarks;
- use visible decrypted secret bookmark URLs for bookmark-icon ranking;
- use decrypted secret icon bitmaps only in memory;
- keep all existing logging restrictions.

While a bookmark/folder editor dialog is open, the secret inactivity timer is
paused. This includes nested icon-library dialogs opened from that editor.

Consequences:

- inactivity must not hide secrets while the user is choosing an icon;
- the icon-library dialog does not need to close itself because of inactivity;
- after the editor closes, the inactivity timeout starts from a fresh full
  interval;
- explicit user actions that hide secrets still apply to the whole application;
- if a selected secret icon has already been intentionally chosen for a normal
  bookmark/folder, it may be materialized as a regular icon on save by the
  existing secret-to-regular conversion path.

## What Counts As "In The Library"

Version one should show only icons currently referenced by at least one item in
the relevant visible data set.

Do not show unreferenced/orphan `icon_assets` or `secret_icon_assets` yet.

Reasons:

- orphan assets can remain after edits and deletes;
- showing them would make old abandoned icons unexpectedly reappear;
- they have no usage context for ranking;
- cleanup/orphan browsing can be designed later as a separate maintenance
  feature.

Default bookmark/folder icons are not part of the library because they are
already first-class editor options.

## Deduplication For Display

The library should deduplicate displayed entries by:

```text
source_hash_algorithm + source_hash
```

This deduplication is only for the visible library list. It does not change
storage rules.

If the same source hash exists in both regular and secret icon tables and both
are visible:

- show one library entry;
- keep references to both backing assets in the library entry;
- prefer the backing asset that matches the target item category when the user
  applies the icon.

Example:

- user edits a normal bookmark;
- the selected library entry has both `icon_assets.id` and
  `secret_icon_assets.id`;
- use the regular `icon_assets.id` directly.

If only the opposite category exists:

- normal target + only secret asset: decrypt/copy to `icon_assets` if possible;
- secret target + only regular asset: encrypt/copy to `secret_icon_assets`;
- folder target + only secret asset: decrypt/copy to `icon_assets`, because
  folders are not secret in the current product model.

This keeps database size bounded by the existing per-table hash deduplication
and avoids mutating shared icon blobs.

## Target Kinds

The library must know what kind of item is being edited:

```text
Bookmark normal target
Bookmark secret target
Folder target
```

Folder targets always apply a regular `icon_asset_id`.

Bookmark targets apply:

- `icon_asset_id` when the bookmark is normal;
- `secret_icon_asset_id` when the bookmark is secret.

The editor may change a bookmark's secret checkbox before save. Therefore the
selected library icon must be applied according to the final save state, not the
state at the time the library dialog opened.

## Bookmark Icon Ranking

When choosing an icon for a bookmark, sort library icons by the best host match
between:

- the URL currently typed in the bookmark editor;
- URLs of visible bookmarks that use that icon.

For each library icon:

1. Find all visible bookmarks using the icon.
2. For every such bookmark:
   - parse the bookmark URL host;
   - parse the target URL host;
   - compute how many host labels match from right to left.
3. Use the maximum local score as the icon's priority score.

Examples:

```text
target: docs.google.com
used:   mail.google.com
score:  2  (google, com)

target: news.ycombinator.com
used:   ycombinator.com
score:  2  (ycombinator, com)

target: example.com
used:   other.com
score:  1  (com)

target: localhost
used:   localhost
score:  1

target: 127.0.0.1
used:   127.0.0.1
score:  1
```

Implementation details:

- reuse `BookmarkUrlNormalizer` rules so typed addresses without scheme can be
  ranked similarly to opening/fetching;
- compare hosts case-insensitively;
- ignore scheme and port for ranking;
- trim trailing dot from host names;
- for `localhost` and IP addresses, treat exact host match as `1`, otherwise
  `0`;
- for normal DNS names, split by dot and count equal labels from right to left;
- do not add a public-suffix dependency in version one.

The lack of a public-suffix list means `example.co.uk` and `other.co.uk` share
two labels (`co`, `uk`) even though that is not a user-meaningful domain match.
This is an accepted v1 tradeoff. A future version can use a public suffix list
if ranking quality requires it.

Icons with no visible bookmark usage get score `0` for bookmark targets.

Suggested tie-breakers for bookmark targets:

1. Higher host match score.
2. More visible bookmark usages.
3. Icons that are also used by folders, if still tied.
4. Newer `created_at_utc`.
5. Stable id ordering for deterministic tests.

## Folder Icon Ranking

When choosing an icon for a folder:

1. Show icons used by at least one visible folder first.
2. Then show icons used only by bookmarks.

Suggested tie-breakers:

1. Folder-used group before bookmark-only group.
2. More visible folder usages.
3. More total visible usages.
4. Newer `created_at_utc`.
5. Stable id ordering for deterministic tests.

Secret icons can appear in the folder icon library only when secrets are visible.
If the user selects a secret icon for a folder, it must be copied/decrypted into
regular `icon_assets` because folders cannot reference `secret_icon_assets`.

## Data Shape

Suggested application-level records:

```csharp
public enum IconLibraryTargetKind
{
    Bookmark,
    Folder
}

public sealed record IconLibraryRequest(
    IconLibraryTargetKind TargetKind,
    string? TargetUrl,
    bool TargetWillBeSecret);

public sealed record IconLibraryItem(
    string SourceHashAlgorithm,
    string SourceHash,
    string? RegularIconAssetId,
    string? SecretIconAssetId,
    IImage Preview,
    int PriorityScore,
    int BookmarkUsageCount,
    int FolderUsageCount,
    DateTimeOffset CreatedAtUtc);
```

Keep raw hashes out of logs and UI. They are internal correlation keys only.

The exact API can change, but it must preserve these concepts:

- one display item can point to a regular asset, a secret asset, or both;
- the item knows whether it is appropriate for the current target;
- the UI receives an already decoded preview image;
- ranking is computed outside XAML.

## Storage Access Direction

Avoid a schema migration for v1.

The existing schema already has the required data:

- item references to icon assets;
- icon source hashes;
- processed icon timestamps;
- encrypted secret icon payloads;
- bookmark URLs after visible projection.

Implementation can initially build the library from:

- current visible bookmark tree snapshot/projection;
- existing `IBookmarkTreeStore.GetIconAsset(...)`;
- existing `IBookmarkTreeStore.GetSecretIconAsset(...)`;
- existing `BookmarkIconImageCache`.

If performance becomes an issue, add batch query methods later:

```csharp
IReadOnlyList<BookmarkIconAssetRecord> GetIconAssetsByIds(IReadOnlySet<string> ids);
IReadOnlyList<SecretIconAssetRecord> GetSecretIconAssetsByIds(IReadOnlySet<string> ids);
```

Do not add batch methods first unless the simple implementation becomes noisy or
slow.

## Applying A Library Selection

`BookmarkIconSelection` currently represents current/default/uploaded/favicon
states. It should be extended with a library selection that can carry backing
asset ids:

```text
Library icon selection:
- regular icon asset id, optional;
- secret icon asset id, optional;
- source hash metadata, internal only if useful.
```

When saving:

### Normal Bookmark Or Folder Target

1. If selected library item has a regular asset id, use it directly.
2. Else if it has only a secret asset id:
   - ensure the runtime secret key is available;
   - convert secret icon to regular icon through `SecretIconAssetService`;
   - use the resulting regular asset id.

### Secret Bookmark Target

1. If selected library item has a secret asset id, use it directly.
2. Else if it has only a regular asset id:
   - convert regular icon to encrypted secret icon through
     `SecretIconAssetService`;
   - use the resulting secret asset id.

This mirrors the current normal/secret bookmark conversion rules.

## UI Direction

Add a "Library" icon option to `BookmarkEditorDialog`.

Recommended v1 UI:

- a separate modal `IconLibraryDialog`;
- scrollable icon grid or wrap panel;
- same project-owned visual style as other dialogs;
- no Avalonia default visual theme leakage;
- shows icons only, optionally with subtle selection/hover state;
- `Esc` closes without selection;
- clicking an icon returns a library selection and preview to the editor.

For bookmark editors:

- open the library using the URL currently typed in the URL field;
- if the URL is empty or invalid, still show the library with all scores `0`;
- ranking should update on every new open, not while the dialog is already open.

For folder editors:

- use folder ranking.

If the library is empty:

- show a small localized empty state;
- leave current/default/upload/favicon options available.

## Privacy And Logging

Never log:

- bookmark URLs;
- bookmark titles;
- folder titles;
- local icon file paths;
- raw hashes;
- secret generation IDs;
- decrypted icon bytes;
- encrypted payloads;
- ranking host values.

Useful logs may include only non-sensitive counts and outcomes, for example:

- icon library opened for target kind;
- number of regular icon candidates;
- number of secret icon candidates included or excluded;
- number of deduplicated display items;
- icon library selection applied;
- icon library selection failed due to missing secret runtime key.

## Known Risks And Tradeoffs

- Host ranking without a public suffix list is approximate.
- Including secret icons requires careful refresh/close behavior when secrets
  hide.
- Showing only referenced icons avoids orphan clutter but means an icon can
  disappear from the library after no item uses it.
- Cross-category selection may copy icons between regular and secret tables,
  increasing database size by one processed blob per category per source hash.
  This is intentional and bounded by existing deduplication.
- Secret icon source hashes remain plaintext. This is already accepted for the
  encrypted secret icon feature.
- The first UI should prefer simplicity over advanced search/filtering inside
  the icon library.
