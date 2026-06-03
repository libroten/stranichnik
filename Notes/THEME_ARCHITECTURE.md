# Stranichnik Theme Architecture

This document describes the planned light/dark theme architecture for Stranichnik.

It is a design note for future agents. It is not a permanent contract: if implementation reveals a simpler or safer approach, update this document and the related project notes.

## Goal

Add application-level support for light and dark themes.

The user should be able to switch the theme from the top application menu through `Service -> Appearance`.

The initial supported modes are:

- `Light`
- `Dark`

The current default should remain visually equivalent to the existing light UI.

## Important Constraint

The theme system must be project-owned and written from scratch.

Do not use Avalonia's built-in light/dark theme switching as the source of behavior:

- Do not use `RequestedThemeVariant` as the app theme state.
- Do not use `ThemeVariant.Light` / `ThemeVariant.Dark` as the app theme model.
- Do not rely on OS theme auto-detection in the first implementation.
- Do not rely on FluentTheme's light/dark palette to style Stranichnik controls.

Avalonia's normal primitives are still allowed and expected:

- `ResourceDictionary`
- `DynamicResource`
- `Styles`
- `ControlTemplate`
- brushes, colors, transitions, popups, windows, buttons, text boxes, layout controls

In other words, Avalonia may render the UI, but Stranichnik owns the theme state, the theme palette, and the mapping from theme to UI colors.

## Current Starting Point

The app currently has a custom light visual style with many hardcoded colors in XAML.

Important files:

- `App.axaml`
- `App.axaml.cs`
- `Settings/AppSettings.cs`
- `Settings/AppSettingsService.cs`
- `Views/MainWindow.axaml`
- `Views/MainWindow.axaml.cs`
- dialog XAML files under `Views/`
- localized strings in `Resources/Strings.resx` and `Resources/Strings.ru.resx`

`App.axaml` should not contain `RequestedThemeVariant`. The source of truth is the project-owned theme service.

The app currently uses `FluentTheme`. This can remain as a fallback source of base control behavior while the app still uses Avalonia controls, but it must not be used as the app's visual palette. Visually important controls should continue to use explicit project styles and templates so Avalonia's default theme colors do not leak into the design.

## Core Design

Introduce a small project-owned theming layer:

```text
settings.json
  -> AppSettings.Theme
    -> ThemeService
      -> ThemePalette
        -> application resource keys
          -> XAML DynamicResource bindings
```

The theme service should:

- Normalize persisted theme values.
- Default to `Light` for missing or invalid values.
- Keep the current theme mode in memory.
- Apply a project-owned palette to application-level resources.
- Save the selected mode through the existing settings service when the user changes it.

The theme service should not:

- Change `Application.RequestedThemeVariant`.
- Ask Avalonia or the operating system which theme should be active.
- Use Avalonia theme variants as model values.

## Suggested Types

Suggested namespace:

- `Stranichnik.Theming`

Suggested types:

- `ThemeMode`
- `ThemePalette`
- `ThemeService`

Possible shape:

```csharp
public enum ThemeMode
{
    Light,
    Dark
}
```

`ThemeService` should expose pure/testable methods for normalization:

```csharp
public static ThemeMode NormalizeTheme(string? theme);
public static string ToSettingsValue(ThemeMode theme);
```

It should also expose an apply method for UI startup and runtime changes:

```csharp
public static void Apply(ThemeMode theme);
```

The method can update `Application.Current.Resources` keys with brushes from the chosen project palette.

## Persisted Setting

Extend `Settings/AppSettings.cs` with:

```csharp
public string Theme { get; set; } = string.Empty;
```

Recommended persisted values:

- `light`
- `dark`

Use lowercase invariant strings in `settings.json`.

Invalid, missing, or unknown values should normalize to `Light`.

## Theme Resources

The app should migrate from raw color literals to semantic resource keys.

Use `DynamicResource` for any resource that must update when the theme changes at runtime.

Use project-owned semantic names instead of visual names tied to one palette.

Recommended resource groups:

### Application Surfaces

- `AppBackgroundBrush`
- `AppTopBarBackgroundBrush`
- `AppTopBarBorderBrush`
- `WorkAreaBackgroundBrush`
- `WorkAreaBorderBrush`
- `SurfaceBrush`
- `SurfaceMutedBrush`
- `SurfaceRaisedBrush`
- `SeparatorBrush`

### Text

- `TextPrimaryBrush`
- `TextSecondaryBrush`
- `TextMutedBrush`
- `TextOnAccentBrush`
- `TextErrorBrush`
- `PlaceholderTextBrush`
- `CaretBrush`

### Interactive States

- `HoverBackgroundBrush`
- `PressedBackgroundBrush`
- `FocusBorderBrush`
- `SelectedBackgroundBrush`
- `DisabledOpacity`

### Tree And Folder UI

- `FolderRowBackgroundBrush`
- `FolderRowHoverBackgroundBrush`
- `FolderContainerBorderBrush`
- `TreeRowHoverBackgroundBrush`
- `DragSourceBackgroundBrush`
- `DragDimmedOpacity`
- `DropPlaceholderBackgroundBrush`
- `DropPlaceholderHoverBackgroundBrush`
- `DropPlaceholderBorderBrush`
- `GlyphBrush`

### Search

- `SearchBoxBackgroundBrush`
- `SearchBoxBorderBrush`
- `SearchIconBrush`
- `SearchClearButtonForegroundBrush`
- `SearchEmptyTextBrush`

### URL

- `UrlTextBrush`
- `UrlHoverTextBrush`
- `UrlUnderlineBrush`

### Actions

- `ActionOpenBrush`
- `ActionOpenHoverBackgroundBrush`
- `ActionAddBookmarkBrush`
- `ActionAddBookmarkHoverBackgroundBrush`
- `ActionAddFolderBrush`
- `ActionAddFolderHoverBackgroundBrush`
- `ActionEditBrush`
- `ActionEditHoverBackgroundBrush`
- `ActionDeleteBrush`
- `ActionDeleteHoverBackgroundBrush`

### Dialogs

- `DialogBackgroundBrush`
- `DialogTextPrimaryBrush`
- `DialogTextSecondaryBrush`
- `DialogInputBackgroundBrush`
- `DialogInputBorderBrush`
- `DialogButtonBackgroundBrush`
- `DialogButtonHoverBackgroundBrush`
- `DialogButtonBorderBrush`
- `DialogAccentButtonBackgroundBrush`
- `DialogAccentButtonHoverBackgroundBrush`
- `DialogDangerButtonBackgroundBrush`
- `DialogDangerButtonHoverBackgroundBrush`

## Palette Application Strategy

Prefer a single source of truth in code:

```text
ThemePalettes.Light
ThemePalettes.Dark
```

Each palette should provide the values for all semantic resource keys.

At apply time, update `Application.Current.Resources[key]` for every semantic brush key.

This keeps the runtime mechanism explicit and easy to inspect. It also avoids relying on Avalonia theme variants or external theme dictionaries.

If XAML dictionaries are used for readability, they must still be project-owned dictionaries, not Avalonia's built-in theme variant dictionaries.

## Startup Flow

At startup:

1. Load `AppSettings`.
2. Apply language as today.
3. Normalize `settings.Theme`.
4. Apply the corresponding project theme before creating `MainWindow`.
5. Create the storage/search/view model and show `MainWindow`.

The theme should be applied before the first window is created to avoid a visible light-to-dark flash.

## Runtime Switching Flow

Clicking `Service -> Appearance` should open an appearance dialog.

Recommended first implementation:

- Create `AppearanceDialog`.
- Add an `Appearance` or `Theme` section.
- Show `Light` and `Dark` options as custom-styled selectable buttons.
- Preselect the current theme.
- `Cancel` closes without saving.
- `Save` stores the selected theme and applies it immediately.

This keeps the current top menu structure simple and avoids nested menu complexity.

Later, if the settings window grows, the same dialog can host sync, encryption, and other preferences.

## Localization

Add localized UI strings for:

- Settings dialog title.
- Appearance section label.
- Theme label.
- Light theme option.
- Dark theme option.
- Optional short descriptions if the UI needs them.

Do not hardcode visible UI text directly in the settings dialog.

## Migration Strategy

Avoid a risky one-shot rewrite of every visual color.

Recommended order:

1. Add theme service, theme mode, palettes, and persisted setting.
2. Add application-level resource keys for the light palette.
3. Convert `MainWindow.axaml` to semantic `DynamicResource` values.
4. Confirm that the light theme still looks like the current UI.
5. Add dark palette values.
6. Add settings dialog and runtime switching.
7. Convert dialog XAML files to the same semantic resources.
8. Add tests for pure theme normalization and settings behavior.

Important: while migrating, do not leave controls half-owned by Avalonia theme colors. If a control has custom hover/focus/pressed states in light mode, define the corresponding dark-mode resources too.

## Tests

Recommended unit tests:

- Missing theme setting normalizes to `Light`.
- Unknown theme setting normalizes to `Light`.
- `light` normalizes to `Light`.
- `dark` normalizes to `Dark`.
- Settings serialization preserves the selected theme.

Theme application itself may be mostly UI behavior and can be validated manually unless it is factored into a pure resource map builder.

## Manual Verification Checklist

Ask the user to run checks. The assistant must not run build, tests, format, or the app.

Manual UI checks:

- New app starts in light theme by default.
- Selecting dark theme applies without restart.
- Selected theme persists after restart.
- Main menu bar and popups are readable in both themes.
- Search bar and search results are readable in both themes.
- Bookmark tree rows, folder containers, hover states, action buttons, URL hover, DnD ghost, placeholders, and dimmed rows are readable in both themes.
- Dialogs are readable in both themes.
- No default Avalonia gray/blue/black theme colors visibly leak into custom controls.

Command checks for the user:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
dotnet run
```

## Known Pitfalls

- `StaticResource` will not update when the theme changes at runtime. Use `DynamicResource` for theme-dependent brushes.
- Existing hardcoded colors are numerous; missing one can produce unreadable text in dark mode.
- Built-in Avalonia control templates can still show default theme colors on hover/focus/pressed states. Important controls should keep explicit templates/styles.
- Applying the theme after creating the main window may cause visible flashing.
- Dark theme needs separate hover/focus/action colors, not just inverted light colors.
- Search results and tree rows should share the same semantic row/action resources where possible to avoid drift.

## Current Implementation

The first implementation is project-owned and lives in:

- `Theming/ThemeMode.cs`
- `Theming/ThemeResourceKeys.cs`
- `Theming/ThemePalette.cs`
- `Theming/ThemePalettes.cs`
- `Theming/ThemeService.cs`

The selected theme is persisted in:

- `Settings/AppSettings.cs`
- `settings.json`

`App.axaml.cs` applies the saved theme before creating `MainWindow`.

Theme switching is available from:

- `Service -> Appearance`

The appearance dialog is implemented in:

- `Views/AppearanceDialog.axaml`
- `Views/AppearanceDialog.axaml.cs`

Main window and dialog XAML files use semantic `DynamicResource` brushes for theme-dependent colors.

## Future Extensions

Possible later additions:

- `System` theme mode.
- Accent color customization.
- Per-theme density or contrast settings.
- Theme preview inside settings.

Do not implement these in the first light/dark stage unless the user explicitly asks.
