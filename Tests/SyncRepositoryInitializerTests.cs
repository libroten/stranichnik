using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncRepositoryInitializerTests
{
    [Fact]
    public async Task EnsureInitializedAsync_creates_manifest_for_empty_remote()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var initializer = CreateInitializer(transport, serializer);

        var result = await initializer.EnsureInitializedAsync(CancellationToken.None);

        Assert.Equal(SyncRepositoryInitializationStatus.Ready, result.Status);
        Assert.NotNull(result.Manifest);
        Assert.Equal("repository-id", result.Manifest.RepositoryId);
        Assert.Equal("device-id", result.Manifest.CreatedByDeviceId);
        Assert.All(
            SyncRemoteRepositoryLayout.RequiredDirectories,
            directory => Assert.True(transport.DirectoryExists(directory)));

        var manifestBytes = await transport.GetAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            CancellationToken.None);
        Assert.NotNull(manifestBytes);
        Assert.Equal(result.Manifest, serializer.Deserialize<SyncManifestDto>(manifestBytes));
    }

    [Fact]
    public async Task EnsureInitializedAsync_keeps_existing_supported_manifest()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var manifest = CreateManifest(repositoryId: "existing-repository");
        await transport.PutAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            serializer.Serialize(manifest),
            expectedEtag: null,
            createOnly: true,
            cancellationToken: CancellationToken.None);
        var initializer = CreateInitializer(transport, serializer);

        var result = await initializer.EnsureInitializedAsync(CancellationToken.None);

        Assert.Equal(SyncRepositoryInitializationStatus.Ready, result.Status);
        Assert.Equal(manifest, result.Manifest);
    }

    [Fact]
    public async Task EnsureInitializedAsync_recovers_when_manifest_is_created_concurrently()
    {
        var transport = new ConcurrentManifestTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        transport.ManifestBytes = serializer.Serialize(CreateManifest(repositoryId: "concurrent-repository"));
        var initializer = CreateInitializer(transport, serializer);

        var result = await initializer.EnsureInitializedAsync(CancellationToken.None);

        Assert.Equal(SyncRepositoryInitializationStatus.Ready, result.Status);
        Assert.Equal("concurrent-repository", result.Manifest?.RepositoryId);
    }

    [Fact]
    public async Task EnsureInitializedAsync_rejects_unsupported_manifest_version()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var manifest = CreateManifest(formatVersion: SyncRemoteObjectConstants.FormatVersion + 1);
        await transport.PutAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            serializer.Serialize(manifest),
            expectedEtag: null,
            createOnly: true,
            cancellationToken: CancellationToken.None);
        var initializer = CreateInitializer(transport, serializer);

        var result = await initializer.EnsureInitializedAsync(CancellationToken.None);

        Assert.Equal(SyncRepositoryInitializationStatus.UnsupportedRepositoryVersion, result.Status);
        Assert.Equal(manifest, result.Manifest);
    }

    [Fact]
    public async Task EnsureInitializedAsync_rejects_invalid_manifest()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            Encoding.UTF8.GetBytes("""{"schema":"wrong"}"""),
            expectedEtag: null,
            createOnly: true,
            cancellationToken: CancellationToken.None);
        var initializer = CreateInitializer(transport, serializer);

        var result = await initializer.EnsureInitializedAsync(CancellationToken.None);

        Assert.Equal(SyncRepositoryInitializationStatus.InvalidRepository, result.Status);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public async Task EnsureInitializedAsync_manifest_does_not_contain_user_content_fields()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var initializer = CreateInitializer(transport, serializer);

        await initializer.EnsureInitializedAsync(CancellationToken.None);

        var manifestBytes = await transport.GetAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            CancellationToken.None);
        Assert.NotNull(manifestBytes);
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        Assert.DoesNotContain("\"title\"", manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"url\"", manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"encryptedPayload\"", manifestJson, StringComparison.Ordinal);
    }

    private static SyncRepositoryInitializer CreateInitializer(
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer)
    {
        return new SyncRepositoryInitializer(
            transport,
            serializer,
            new SyncLocalIdentity("database-id", "device-id"),
            clock: () => CreatedAt,
            repositoryIdFactory: () => "repository-id");
    }

    private static SyncManifestDto CreateManifest(
        string repositoryId = "repository-id",
        int formatVersion = SyncRemoteObjectConstants.FormatVersion)
    {
        return new SyncManifestDto(
            SyncRemoteObjectConstants.ManifestSchema,
            formatVersion,
            repositoryId,
            CreatedAt,
            "device-id",
            SyncRemoteObjectConstants.FormatVersion);
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private sealed class ConcurrentManifestTransport : InMemoryWebDavSyncTransport
    {
        private int _manifestGetCount;

        public byte[]? ManifestBytes { get; set; }

        public override Task<byte[]?> GetAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            if (relativePath == SyncRemoteRepositoryLayout.ManifestPath && ++_manifestGetCount == 1)
                return Task.FromResult<byte[]?>(null);

            return relativePath == SyncRemoteRepositoryLayout.ManifestPath
                ? Task.FromResult(ManifestBytes)
                : base.GetAsync(relativePath, cancellationToken);
        }

        public override Task<SyncPutResult> PutAsync(
            string relativePath,
            byte[] bytes,
            string? expectedEtag,
            bool createOnly,
            CancellationToken cancellationToken)
        {
            if (relativePath == SyncRemoteRepositoryLayout.ManifestPath && createOnly)
                return Task.FromResult(SyncPutResult.PreconditionFailed("\"manifest\""));

            return base.PutAsync(relativePath, bytes, expectedEtag, createOnly, cancellationToken);
        }
    }
}
