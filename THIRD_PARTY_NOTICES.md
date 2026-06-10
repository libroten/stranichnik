# Third-Party Notices

This file tracks third-party components used by Stranichnik. It is intended to
be useful both in the source repository and as part of binary release artifacts.

Keep this file up to date whenever NuGet packages, bundled assets, fonts,
native libraries, copied source files, generated resources based on third-party
material, or other external components are added, removed, or replaced.

This document is an engineering inventory, not legal advice. Before publishing a
binary release, verify the final packaged output and include this file plus the
license and notice texts from `THIRD_PARTY_LICENSES/`.

## Runtime NuGet Packages

Direct runtime package references:

| Component | Version | License | Usage |
| --- | --- | --- | --- |
| AngleSharp | 1.4.0 | MIT | HTML parsing for bookmark metadata and favicon discovery. |
| Avalonia | 12.0.3 | MIT | Cross-platform UI framework. |
| Avalonia.Desktop | 12.0.3 | MIT | Desktop lifetime/platform integration. |
| Avalonia.Fonts.Inter | 12.0.3 | MIT | Inter font integration used by Avalonia. |
| Avalonia.Themes.Fluent | 12.0.3 | MIT | Avalonia theme/control resources used as a base behavior layer. |
| CommunityToolkit.Mvvm | 8.4.1 | MIT | MVVM helpers. |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | SQLite access. |

Notable transitive runtime packages:

| Component | Version | License | Notes |
| --- | --- | --- | --- |
| Avalonia.* platform/rendering packages | 12.0.3 | MIT | Pulled by Avalonia desktop/runtime packages. |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD-style license file in package | Windows native ANGLE binaries. Preserve notices for binary distribution. |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia build tooling. |
| Avalonia.Remote.Protocol | 12.0.3 | MIT | Avalonia remote/dev protocol dependency. |
| Avalonia.FreeDesktop and Avalonia.FreeDesktop.AtSpi | 12.0.3 | MIT | Linux desktop integration dependencies. |
| HarfBuzzSharp and native assets | 8.3.1.3 | MIT | Text shaping dependency. |
| MicroCom.Runtime | 0.11.4 | MIT | Avalonia interop dependency. |
| Microsoft.Data.Sqlite.Core | 8.0.11 | MIT | SQLite provider core. |
| SkiaSharp and native assets | 3.119.4-preview.1.1 | MIT | Rendering dependency. |
| SQLitePCLRaw.* | 2.1.6 | Apache-2.0 | SQLite native/provider dependency. |
| System.IO.Pipelines | 8.0.0 | MIT | Transitive dependency. |
| System.Memory | 4.5.3 | .NET Foundation license URL in package metadata | Transitive dependency. |
| Tmds.DBus.Protocol | 0.92.0 | MIT | Linux desktop integration dependency. |

## Test-Only NuGet Packages

These packages are used by test projects and are not runtime application
features:

| Component | Version | License |
| --- | --- | --- |
| Microsoft.CodeCoverage | 17.12.0 | MIT |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.12.0 | MIT |
| Microsoft.TestPlatform.TestHost | 17.12.0 | MIT |
| Newtonsoft.Json | 13.0.1 | MIT |
| System.Reflection.Metadata | 1.6.0 | .NET Foundation license URL in package metadata |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | xUnit license URL in package metadata |
| xunit.analyzers | 1.16.0 | Apache-2.0 |
| xunit.assert | 2.9.2 | Apache-2.0 |
| xunit.core | 2.9.2 | Apache-2.0 |
| xunit.extensibility.core | 2.9.2 | Apache-2.0 |
| xunit.extensibility.execution | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |

## Local Project Components

| Component | Location | Notes |
| --- | --- | --- |
| Stranichnik.Search | `Stranichnik.Search/` | First-party library in this repository. |
| Project SVG action icons | `icons/*.svg`, `Assets/Icons/ActionIcons.axaml` | First-party project assets. |
| Default bookmark/folder SVG icons | `Assets/Icons/*.svg` | First-party project assets. |
| Application icon | `Assets/app-icon.png` | First-party project asset. Used as the window icon for `dotnet run`. |

## Removed Components

| Component | Previous version | Reason |
| --- | --- | --- |
| AvaloniaUI.DiagnosticsSupport | 2.2.1 | Removed because the package did not declare a license in local NuGet metadata and the app does not directly use it. |

## Release Packaging Notes

Binary release archives/packages should include:

- `LICENSE`;
- `THIRD_PARTY_NOTICES.md`;
- `THIRD_PARTY_LICENSES/`;
- any additional license or notice files discovered in the final publish output.

Before publishing a binary release:

1. run `dotnet publish` for the exact target platform/runtime that will be
   distributed;
2. inspect the generated publish directory, not only `.csproj` files;
3. look for third-party managed assemblies, native libraries, fonts, resources,
   and copied assets;
4. compare those files with the runtime package inventory in this document;
5. if new third-party components appear, update this document and
   `THIRD_PARTY_LICENSES/` in the same change;
6. package the license files somewhere users can access after installation.

The application does not currently have an in-app "Third-party licenses" screen.
For the current dependency set, the intended release approach is to ship the
license and notice files on disk with the application. An in-app viewer can be
added later as a usability feature, especially for packaged macOS/Windows/Linux
installers where users may not naturally browse the installation directory.

## Maintenance Checklist

When dependency or external asset usage changes:

- update this file;
- update `THIRD_PARTY_LICENSES/` if runtime release obligations change;
- check direct and transitive NuGet license metadata after restore;
- update `README.md` if the change affects contributors or release notes;
- update `Notes/PROJECT_STATE.md` or architecture notes when the change affects project structure;
- for release packaging, verify the final publish output and include required full license texts/notices for bundled binaries and native libraries.
