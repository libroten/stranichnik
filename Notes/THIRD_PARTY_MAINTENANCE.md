# Third-Party Maintenance Notes

This note is for maintainers and future agents. It is not intended to be shown
as a legal document inside the application.

User-facing release documents are:

- `LICENSE`;
- `THIRD_PARTY_NOTICES.md`;
- `THIRD_PARTY_LICENSES/*.txt`.

The application displays those documents through `Stranichnik -> About`. The
About screen reads them from the application output directory, so the `.csproj`
copy rules, release package contents, and in-app viewer behavior must stay in
sync.

## Repository Roles

- `THIRD_PARTY_NOTICES.md` is the user-facing third-party component summary for
  runtime application dependencies.
- `THIRD_PARTY_LICENSES/` contains full license and notice text files intended
  for binary releases and the in-app About screen.
- `Notes/NUGET_LICENSE_METADATA.md` records package metadata observed during
  dependency review.
- This file records the maintenance workflow.

## Before Publishing A Binary Release

1. Run `dotnet publish` for the exact target platform/runtime that will be
   distributed.
2. Inspect the generated publish directory, not only `.csproj` files.
3. Look for third-party managed assemblies, native libraries, fonts, resources,
   and copied assets.
4. Compare those files with the runtime package inventory in
   `THIRD_PARTY_NOTICES.md`.
5. If new third-party components appear, update `THIRD_PARTY_NOTICES.md`,
   `THIRD_PARTY_LICENSES/`, and `Notes/NUGET_LICENSE_METADATA.md` in the same
   change.
6. Package `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `THIRD_PARTY_LICENSES/`
   somewhere users can access after installation.
7. Verify that `Stranichnik -> About` can open the bundled project license,
   third-party notice file, and third-party license texts from the application
   output directory.

## Maintenance Checklist

When dependency or external asset usage changes:

- update `THIRD_PARTY_NOTICES.md`;
- update `THIRD_PARTY_LICENSES/` if runtime release obligations change;
- update `Notes/NUGET_LICENSE_METADATA.md` after checking direct and transitive
  NuGet package metadata;
- update `README.md` if the change affects contributors or release notes;
- update `Notes/PROJECT_STATE.md` or architecture notes when the change affects
  project structure;
- keep `Stranichnik -> About` in sync with the release license/notice files;
- verify the final publish output before a real binary release.

## Test-Only NuGet Packages

These packages are used by test projects and are not runtime application
features. They usually should not appear in the in-app About screen unless they
are bundled into a release artifact.

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

## Removed Components

| Component | Previous version | Reason |
| --- | --- | --- |
| AvaloniaUI.DiagnosticsSupport | 2.2.1 | Removed because the package did not declare a license in local NuGet metadata and the app does not directly use it. |
