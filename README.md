# Stranichnik

Stranichnik is a cross-platform desktop bookmark manager built with C# and Avalonia UI.

The application stores bookmark data in a local SQLite database under the user's application data directory. New databases are empty by default. Bookmarks marked as secret store their sensitive title/URL payload encrypted in SQLite and are hidden until unlocked in the app.

## AI-Generated Project

> **Warning**
>
> This project is fully generated with the help of AI. Code, structure, and documentation should be reviewed carefully before relying on them in production or using them as a reference implementation.

## Requirements

Install the .NET 8 SDK:

- Download page: https://dotnet.microsoft.com/download/dotnet/8.0
- Verify installation:

```bash
dotnet --version
```

The command should print an `8.x.x` SDK version or newer compatible SDK.

Avalonia and test dependencies are restored automatically through NuGet when running `dotnet restore`, `dotnet build`, `dotnet run`, or `dotnet test`.

## Third-Party Components

Runtime third-party NuGet packages and notable transitive dependencies are
summarized for users in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
Runtime license texts and package-provided notices intended for binary releases
are kept in [THIRD_PARTY_LICENSES/](THIRD_PARTY_LICENSES/).
The same project license, third-party notice file, and bundled license texts are
available in the application through `Stranichnik -> About`.

When adding, removing, or replacing external packages, copied assets, generated
resources based on third-party material, fonts, native libraries, or other
external components, update these files and the in-app About view in the same
change. Maintainer-only dependency notes live in
[Notes/THIRD_PARTY_MAINTENANCE.md](Notes/THIRD_PARTY_MAINTENANCE.md) and
[Notes/NUGET_LICENSE_METADATA.md](Notes/NUGET_LICENSE_METADATA.md).

## Clone the Repository

```bash
git clone <repository-url>
cd stranichnik
```

Replace `<repository-url>` with the actual Git URL of this repository.

## Restore Dependencies

Restore NuGet packages for the application:

```bash
dotnet restore
```

Restore NuGet packages for tests:

```bash
dotnet restore Tests/Stranichnik.Tests.csproj
```

## Build the Application

From the repository root:

```bash
dotnet build
```

This builds the main Avalonia desktop application project:

```text
Stranichnik.csproj
```

## Run the Application

From the repository root:

```bash
dotnet run
```

This starts the desktop application.

To prefill a newly created database with sample bookmarks, run:

```bash
dotnet run -- --use-sample-data
```

Sample data is inserted only when the SQLite file did not exist before application startup. Existing databases are never reseeded by this flag.

The local database file is created automatically on first run. It is stored in the same application data directory as local settings and logs:

```text
stranichnik.sqlite
settings.json
stranichnik.log
```

The exact application data directory depends on the operating system and current user profile.

## WebDAV Sync

Stranichnik includes first-pass manual WebDAV synchronization. Configure it from:

```text
Stranichnik -> Settings -> Sync
```

The settings UI stores the WebDAV URL, username, and non-secret credential
metadata in `settings.json`. The raw WebDAV password is kept behind the sync
credential abstraction. When the password is entered and settings are saved,
Stranichnik tries to remember it through the operating system credential store.
If that is unavailable, the app asks before using an obfuscated local fallback
file and shows a persistent warning in the sync settings.

The sync settings section also has a reset action that clears the saved WebDAV
URL, username, password metadata, and stored WebDAV password for this device.

Sync stores portable JSON objects on the WebDAV server instead of uploading the
local SQLite database file. Secret bookmark payloads and secret icon bytes remain
encrypted in the remote sync data.

Recommended manual run with logs while testing sync:

```bash
dotnet run -- --print-logs-to-console
```

To manually test the fallback path where the operating system credential store is
unavailable:

```bash
dotnet run -- --print-logs-to-console --simulate-unavailable-system-credential-store
```

## Run Tests

Run the test project explicitly:

```bash
dotnet test Tests/Stranichnik.Tests.csproj
```

Tests are located in the `Tests/` directory.

## Code Quality Checks

The project uses built-in .NET analyzers and `.editorconfig` rules. Analyzer warnings are reported during build.

Check formatting without modifying files:

```bash
dotnet format --verify-no-changes
```

Apply formatting fixes:

```bash
dotnet format
```

## Common Development Commands

Restore, build, and test:

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
```

Run the application:

```bash
dotnet run
```

Clean build outputs:

```bash
dotnet clean
```

## Notes for New Contributors

- The main application targets `net8.0`.
- The UI is built with Avalonia UI.
- The test project lives under `Tests/` and is excluded from the main application project compilation.

## Troubleshooting

### `dotnet` Command Not Found

Install the .NET 8 SDK and restart the terminal.

### Build Fails After Cloning

Run:

```bash
dotnet restore
dotnet build
```

If test dependencies are missing, run:

```bash
dotnet restore Tests/Stranichnik.Tests.csproj
```

### Tests Are Not Found

Make sure you run tests against the test project:

```bash
dotnet test Tests/Stranichnik.Tests.csproj
```

### Application Does Not Start

First confirm that the project builds successfully:

```bash
dotnet build
```

Then run:

```bash
dotnet run
```
