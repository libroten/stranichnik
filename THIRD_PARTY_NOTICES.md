# Third-Party Notices

This file summarizes third-party runtime components used by Stranichnik.

Full license and notice texts are bundled in `THIRD_PARTY_LICENSES/` and are
also available in the application through `Stranichnik -> About`.

This document is a best-effort open-source notice for the current dependency
set. It is not legal advice.

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
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD-style package license file | Windows native ANGLE binaries. |
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

## Bundled License Texts

The following license and notice text files are bundled with the application
when applicable:

- `THIRD_PARTY_LICENSES/Apache-2.0.txt`
- `THIRD_PARTY_LICENSES/Avalonia.Angle.Windows.Natives-LICENSE.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp-LICENSE.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.Linux-LICENSE.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.Linux-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.Win32-LICENSE.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.Win32-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.macOS-LICENSE.txt`
- `THIRD_PARTY_LICENSES/HarfBuzzSharp.NativeAssets.macOS-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/MIT.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp-LICENSE.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.Linux-LICENSE.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.Linux-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.Win32-LICENSE.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.Win32-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.macOS-LICENSE.txt`
- `THIRD_PARTY_LICENSES/SkiaSharp.NativeAssets.macOS-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/System.IO.Pipelines-LICENSE.txt`
- `THIRD_PARTY_LICENSES/System.IO.Pipelines-THIRD-PARTY-NOTICES.txt`
- `THIRD_PARTY_LICENSES/System.Memory-LICENSE.txt`
- `THIRD_PARTY_LICENSES/System.Memory-THIRD-PARTY-NOTICES.txt`
