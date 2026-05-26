# Stranichnik

Stranichnik is a cross-platform desktop bookmark manager built with C# and Avalonia UI.

The project currently uses in-memory sample bookmark data. Persistent storage is planned for a later stage.

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

## Run Tests

Run the test project explicitly:

```bash
dotnet test Tests/Stranichnik.Tests.csproj
```

Tests are located in the `Tests/` directory.

## Common Development Commands

Restore, build, and test:

```bash
dotnet restore
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
