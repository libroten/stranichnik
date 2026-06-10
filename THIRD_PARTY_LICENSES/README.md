# Third-Party License Texts

This directory is intended to be included with binary Stranichnik releases.

It contains:

- canonical license texts for SPDX license expressions used by runtime NuGet packages;
- package-provided license and notice files when they are present in the local NuGet package cache;
- metadata notes for packages that declare a license expression in `.nuspec` but do not ship a separate license file.

The engineering inventory lives in `../THIRD_PARTY_NOTICES.md`.

Before publishing a binary release:

1. run `dotnet publish` for the target platform/runtime;
2. inspect the publish output for third-party `.dll`, `.so`, `.dylib`, `.pdb`, resource, font, and native binary files;
3. compare the output with `../THIRD_PARTY_NOTICES.md`;
4. update this directory if a runtime package, bundled asset, or native dependency changes;
5. include `../LICENSE`, `../THIRD_PARTY_NOTICES.md`, and this directory in the release archive/package, or place equivalent copies where users can access them after installation.

This directory is not legal advice. It is a best-effort open-source release aid
for the current dependency set.
