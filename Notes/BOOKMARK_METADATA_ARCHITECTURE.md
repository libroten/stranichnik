# Bookmark Metadata Fetching Architecture

This note describes the architecture for fetching bookmark page metadata from a pasted URL.

Important: this is not a fixed contract. It records the current preferred design so future agents can continue without reconstructing the chat context. If implementation reveals a simpler or safer approach, update this note and the current plan.

## User Goal

When the user pastes or types a URL in `BookmarkEditorDialog`, Stranichnik should try to discover a human-friendly page title and, when possible, a favicon candidate.

The discovered title should not be forced into the title field automatically. Instead, the dialog should show it as a clickable suggestion below the title input. The suggestion should visually behave like bookmark URLs in the main window. When the user clicks the suggestion, the text is copied into the title input.

While metadata is being fetched, the dialog should remain responsive and show a small throbber/loading indicator.

If a favicon is discovered, downloaded, and processed successfully, the dialog should show it as a pending icon option. The favicon must not be persisted unless the user selects it and saves the dialog.

## Hard Constraint: No Backend

There must be no Stranichnik backend and no external unfurling service.

All work happens inside the desktop application:

- normalize the URL locally;
- make direct `http`/`https` requests from the desktop app;
- read the HTML response locally;
- parse metadata locally;
- update Avalonia UI locally.

Do not introduce a hosted service, API proxy, cloud function, database worker, or third-party link-preview API.

## Current Related Code

Bookmark editing UI:

- `Views/BookmarkEditorDialog.axaml`
- `Views/BookmarkEditorDialog.axaml.cs`

Icon processing and pending icon selection:

- `IconProcessing/`
- `Storage/BookmarkIconAssetRecord.cs`

URL opening and normalization:

- `Opening/BookmarkUrlNormalizer.cs`
- `Opening/BookmarkUrlOpenStatus.cs`

The metadata fetcher should reuse the same conceptual URL rules as opening bookmarks:

- support complete `http://` and `https://` URLs;
- support web addresses without scheme by treating them as `https://...`;
- support localhost, IP addresses, and ports;
- reject unsupported explicit schemes such as `ftp:`, `mailto:`, and `file:`.

## Proposed Components

### BookmarkPageMetadata

File:

```text
Opening/BookmarkPageMetadata.cs
```

Current shape:

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

Description, image, site name, and canonical URL are still non-goals for the current implementation.

### BookmarkMetadataParser

Suggested file:

```text
Opening/BookmarkMetadataParser.cs
```

Responsibility:

- accept HTML text;
- parse page title metadata;
- parse favicon candidates;
- return `BookmarkPageMetadata`.

Preferred title priority:

1. `meta[property="og:title"]`
2. `meta[name="twitter:title"]`
3. `<title>`
4. optional later: JSON-LD / Schema.org `headline` or `name`

Cleanup rules:

- trim leading/trailing whitespace;
- HTML-decode entities;
- collapse repeated whitespace and newlines into a single space;
- ignore empty/whitespace-only titles;
- optionally apply a reasonable max title length for UI safety.

Use a real HTML parser rather than regular expressions. Preferred options:

- `AngleSharp`: modern parser, good CSS selector support.
- `HtmlAgilityPack`: simple and widely used.

Current implementation uses AngleSharp for normal full-document parsing.

For over-large HTML responses, the app intentionally does not parse a full document. It uses bounded fallback parsing against the already downloaded prefix:

- `ParseTitleTagOnly` searches for a complete `<title>...</title>`;
- `ParseIconCandidatesFromPartialHtml` searches for supported `<link ...>` icon tags and adds `/favicon.ico`.

This fallback is deliberately smaller and less complete than normal AngleSharp parsing. It exists to recover useful metadata from common pages where head metadata appears near the beginning.

### BookmarkMetadataFetcher

Suggested file:

```text
Opening/BookmarkMetadataFetcher.cs
```

Responsibility:

- accept raw URL/address text from the dialog;
- use `BookmarkUrlNormalizer.TryNormalizeForOpening`;
- reject unsupported or invalid addresses without throwing into UI code;
- fetch HTML with `HttpClient`;
- enforce timeout and cancellation;
- read only a bounded amount of response data;
- check that the response looks like HTML;
- call `BookmarkMetadataParser`;
- discover favicon candidates;
- try a bounded number of favicon downloads and image-processing attempts;
- return a success/failure result.

Suggested result shape:

```csharp
public sealed record BookmarkMetadataFetchResult(
    bool IsSuccess,
    BookmarkPageMetadata? Metadata,
    BookmarkMetadataFetchFailureReason? FailureReason);
```

Possible failure reasons:

- invalid address;
- unsupported scheme;
- network failure;
- timeout;
- non-HTML response;
- metadata not found;
- response too large;
- cancelled.

The UI does not need to show these failures in the first version. It can silently hide the suggestion and stop the throbber.

Favicon failures are not fatal for title fetching. If title metadata succeeds but favicon download or processing fails, the dialog should still show the title suggestion and simply omit the favicon option.

## Async And UI Thread Model

The UI must stay responsive.

Recommended behavior:

- network requests are done with `HttpClient.SendAsync` / `GetAsync` asynchronously;
- use `HttpCompletionOption.ResponseHeadersRead` so the app can stop after reading enough bytes;
- pass a `CancellationToken` from the dialog;
- parse HTML off the UI thread if parsing can be non-trivial, for example through `Task.Run(..., cancellationToken)`;
- return to the Avalonia UI thread before changing controls.

In Avalonia code-behind, an `async` event handler or debounced async method normally resumes on the UI context after `await` unless explicitly using `ConfigureAwait(false)` in the caller. The service layer may use `ConfigureAwait(false)` internally, but UI updates must happen from dialog code after awaiting or through `Dispatcher.UIThread.Post`.

## BookmarkEditorDialog Integration

The dialog should not block while fetching.

Suggested private state:

- `CancellationTokenSource? _metadataFetchCancellation;`
- `int _metadataFetchVersion;`
- `bool _titleEditedByUser;`
- `string? _suggestedTitle;`

Suggested UI elements under the title input:

- throbber/loading indicator, visible while a fetch is active;
- clickable title suggestion, visible when a title was found;
- optional small failure text only for diagnostics, not required in the first version.

The suggestion should visually resemble a bookmark URL in the main window:

- subdued link-like text color;
- dashed underline if practical;
- accent/hover color on pointer hover;
- pointer cursor behavior if available in Avalonia.

Interaction rules:

- URL text changes trigger a debounced metadata lookup.
- A new URL change cancels the previous request.
- Stale results are ignored using the version counter.
- The fetch should start only when the text can be normalized into a supported web URL.
- The title input is not overwritten automatically.
- If a title is found, show it as a clickable suggestion below the title input.
- Clicking the suggestion sets `TitleTextBox.Text` to the suggested title and marks the title as user-edited.
- If the user edits the title manually, the current suggestion may remain visible, but it must not overwrite the typed title.
- If the user changes the URL again, clear the previous suggestion and start a new lookup.
- If a favicon is discovered, downloaded, and processed successfully, show it as a pending icon option.
- Persist the favicon only if the user selects it and saves the dialog.
- Cancelling the dialog must not store downloaded favicon bytes in SQLite.
- `Esc` behavior remains unchanged.

## Network Rules

Use conservative defaults:

- timeout: around 3-5 seconds;
- max HTML bytes: around 512 KiB for the first version;
- if the response exceeds the max HTML size, do not continue reading the full response; try a bounded fallback by searching for a complete `<title>...</title>` tag in the already downloaded prefix;
- if the response exceeds the max HTML size, also try to discover favicon link tags in the already downloaded prefix;
- favicon max bytes: currently 256 KiB per candidate;
- favicon candidate cap: currently 4 candidates;
- redirect handling: allow normal HTTP redirects, but keep the platform default or configure a modest maximum;
- request method: `GET`, because metadata lives in HTML;
- accepted schemes: `http` and `https` only;
- request headers: use a modest `User-Agent` such as `Stranichnik/<version>` and `Accept: text/html,application/xhtml+xml`;
- do not send cookies, credentials, or authentication headers;
- do not execute JavaScript;
- do not load images, CSS, iframes, scripts, or subresources.

Favicon handling:

- favicon candidates are parsed from normal full HTML or from the bounded fallback prefix;
- candidate downloads are direct desktop app requests;
- candidate downloads use size limits and cancellation;
- candidate images are processed through the icon-processing boundary before being offered in the dialog;
- favicon URLs, bookmark URLs, discovered titles, raw image bytes, and hashes must not be written to logs.

Content handling:

- parse `text/html` and `application/xhtml+xml`;
- optionally accept missing `Content-Type` if the first bytes look like HTML;
- reject obvious non-HTML responses such as images, PDFs, archives, and JSON for the first version.

Encoding:

- use response charset if present;
- if the response charset is unknown or unsupported by the current runtime, fall back to UTF-8 instead of failing the fetch;
- otherwise use UTF-8 as a first implementation;
- a later improvement can inspect `<meta charset>` or let the chosen HTML parser detect encoding from bytes/stream.

## Testing Strategy

Do not write tests that depend on real internet access.

Parser tests:

- `og:title` is preferred over `twitter:title` and `<title>`;
- `twitter:title` is preferred over `<title>`;
- `<title>` works as fallback;
- empty titles are ignored;
- entities are decoded;
- repeated whitespace is collapsed;
- tags with different attribute order still parse;
- casing variations are handled if the parser supports case-insensitive HTML parsing.
- favicon candidates are extracted and relative URLs are resolved;
- fallback `/favicon.ico` is added;
- partial HTML fallback can extract icon candidates without full-document parsing.

Fetcher tests:

- use a fake `HttpMessageHandler`;
- successful HTML returns parsed metadata;
- invalid address does not send a request;
- unsupported scheme does not send a request;
- non-HTML content is rejected;
- network exception returns failure;
- timeout/cancellation returns failure;
- over-large response is bounded.
- over-large response can still use partial fallback for title and favicon candidates.
- favicon download uses fake HTTP handlers and fake icon processing.

Dialog behavior:

- prefer manual verification for the first version unless a small test seam already exists;
- manually verify that the dialog remains responsive, throbber appears, stale result does not overwrite a newer URL, and suggestion click fills the title field.
- manually verify that a discovered favicon option appears when available, can be selected, and is not persisted after cancelling the dialog.

## Non-Goals For First Version

Do not implement these yet:

- rich preview cards;
- rich preview descriptions/images;
- oEmbed;
- JSON-LD / Schema.org parsing unless it is very cheap after the basic parser is done;
- JavaScript rendering;
- browser automation;
- caching metadata in SQLite;
- background refresh of existing bookmarks;
- automatic title replacement without user click;
- remote unfurling service.
