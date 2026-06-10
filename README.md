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

Third-party NuGet packages, notable transitive dependencies, and bundled project
assets are tracked in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

When adding, removing, or replacing external packages, copied assets, generated
resources based on third-party material, fonts, native libraries, or other
external components, update that file in the same change.

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
