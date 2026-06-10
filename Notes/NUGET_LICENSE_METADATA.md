# NuGet Runtime License Metadata

This file records license metadata observed in local NuGet `.nuspec` files for
runtime packages. Some NuGet packages declare only an SPDX license expression and
do not include a standalone license file in the package contents.

This is a maintainer note, not a user-facing legal document. User-facing release
documents live in `THIRD_PARTY_NOTICES.md` and `THIRD_PARTY_LICENSES/*.txt`.

## Direct Runtime Packages

| Package | Version | License expression | Copyright / authors from package metadata |
| --- | --- | --- | --- |
| AngleSharp | 1.4.0 | MIT | Copyright 2013-2025, AngleSharp. |
| Avalonia | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| Avalonia.Desktop | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| Avalonia.Fonts.Inter | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| Avalonia.Themes.Fluent | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| CommunityToolkit.Mvvm | 8.4.1 | MIT | (c) .NET Foundation and Contributors. All rights reserved. |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | © Microsoft Corporation. All rights reserved. |

## Notable Transitive Runtime Packages

| Package | Version | License expression | Copyright / authors from package metadata |
| --- | --- | --- | --- |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | package license file | See `Avalonia.Angle.Windows.Natives-LICENSE.txt`. |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia build tooling dependency. |
| Avalonia.FreeDesktop | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| Avalonia.FreeDesktop.AtSpi | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| Avalonia.Remote.Protocol | 12.0.3 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| HarfBuzzSharp | 8.3.1.3 | MIT | © Microsoft Corporation. All rights reserved. |
| HarfBuzzSharp.NativeAssets.* | 8.3.1.3 | MIT | © Microsoft Corporation. All rights reserved. See package notice copies. |
| MicroCom.Runtime | 0.11.4 | MIT | Copyright 2021 © Nikita Tsukanov |
| Microsoft.Data.Sqlite.Core | 8.0.11 | MIT | © Microsoft Corporation. All rights reserved. |
| SkiaSharp | 3.119.4-preview.1.1 | MIT | © Microsoft Corporation. All rights reserved. |
| SkiaSharp.NativeAssets.* | 3.119.4-preview.1.1 | MIT | © Microsoft Corporation. All rights reserved. See package notice copies. |
| SQLitePCLRaw.* | 2.1.6 | Apache-2.0 | Copyright 2014-2023 SourceGear, LLC |
| System.IO.Pipelines | 8.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Memory | 4.5.3 | .NET Foundation license URL in package metadata | See package license copy. |
| Tmds.DBus.Protocol | 0.92.0 | MIT | Tom Deseyn |

When this table changes, update `THIRD_PARTY_NOTICES.md` and the package
license/notice copies in `THIRD_PARTY_LICENSES/`.
