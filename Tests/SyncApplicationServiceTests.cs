using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Push;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncApplicationServiceTests
{
    [Fact]
    public async Task SyncNowAsync_initializes_repository_and_runs_pull()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var localStore = new FakeSyncLocalStore(EmptySnapshot());
        var remoteItem = CreateRemoteItem("remote-bookmark", serializer);
        await PutRemoteObjectAsync(transport, serializer, SyncObjectKind.Item, remoteItem.Id, remoteItem);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.SyncNowAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(SyncBlockingReason.None, summary.BlockingReason);
        Assert.Equal(1, summary.DownloadedCount);
        Assert.Single(localStore.AppliedBatches);
        Assert.True(transport.DirectoryExists(SyncRemoteRepositoryLayout.ItemsDirectory));
    }

    [Fact]
    public async Task SyncNowAsync_runs_push_after_pull()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("local-bookmark")
            ]
        });
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.SyncNowAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(1, summary.UploadedCount);
        var remoteBytes = await transport.GetAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "local-bookmark")),
            CancellationToken.None);
        Assert.NotNull(remoteBytes);
        var remoteItem = serializer.Deserialize<SyncItemDto>(remoteBytes);
        Assert.Equal("local-bookmark", remoteItem.Id);
    }

    [Fact]
    public async Task SyncNowAsync_stops_when_repository_version_is_unsupported()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var localStore = new FakeSyncLocalStore(EmptySnapshot());
        await transport.PutAsync(
            SyncRemoteRepositoryLayout.ManifestPath,
            serializer.Serialize(CreateManifest(formatVersion: SyncRemoteObjectConstants.FormatVersion + 1)),
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.SyncNowAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(SyncBlockingReason.UnsupportedRepositoryVersion, summary.BlockingReason);
        Assert.Empty(localStore.AppliedBatches);
    }

    [Fact]
    public async Task SyncNowAsync_postpones_when_local_operation_is_active()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var localStore = new FakeSyncLocalStore(EmptySnapshot());
        var operationGate = new SyncOperationGate();
        using var operation = operationGate.EnterEditorSession();
        var service = CreateService(localStore, transport, serializer, operationGate);

        var summary = await service.SyncNowAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(SyncBlockingReason.LocalOperationActive, summary.BlockingReason);
        Assert.Empty(localStore.AppliedBatches);
        Assert.False(transport.DirectoryExists(SyncRemoteRepositoryLayout.ItemsDirectory));
    }

    private static SyncApplicationService CreateService(
        FakeSyncLocalStore localStore,
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer,
        SyncOperationGate? operationGate = null)
    {
        var initializer = new SyncRepositoryInitializer(
            transport,
            serializer,
            new SyncLocalIdentity("database-id", "device-id"),
            clock: () => Now,
            repositoryIdFactory: () => "repository-id");
        var pullService = new SyncPullService(
            localStore,
            new SyncRemoteObjectReader(transport, serializer),
            clock: () => Now,
            log: _ => { });
        var pushService = new SyncPushService(
            localStore,
            transport,
            serializer,
            new SyncRemoteDtoMapper(serializer, new Sha256SyncContentHasher()),
            clock: () => Now,
            log: _ => { });

        return new SyncApplicationService(
            initializer,
            pullService,
            pushService,
            operationGate ?? new SyncOperationGate(),
            clock: () => Now,
            log: _ => { });
    }

    private static async Task PutRemoteObjectAsync<T>(
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer,
        SyncObjectKind kind,
        string id,
        T value)
    {
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(kind, id)),
            serializer.Serialize(SyncTestDtoHasher.WithContentHash(value, serializer)),
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
    }

    private static SyncLocalSnapshot EmptySnapshot()
    {
        return new SyncLocalSnapshot(
            new SyncLocalIdentity("database", "device"),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);
    }

    private static SyncManifestDto CreateManifest(
        int formatVersion = SyncRemoteObjectConstants.FormatVersion)
    {
        return new SyncManifestDto(
            SyncRemoteObjectConstants.ManifestSchema,
            formatVersion,
            "repository-id",
            Now,
            "device-id",
            SyncRemoteObjectConstants.FormatVersion);
    }

    private static SyncItemSnapshotRecord CreateLocalItem(string id)
    {
        var item = new BookmarkItemRecord(
            id,
            ParentId: null,
            Kind: BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            Title: "Title",
            Url: "https://example.com/",
            IsSecret: false,
            EncryptedPayload: null,
            Metadata: new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                BookmarkSyncState.Dirty,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                ContentHash: null,
                ModifiedDeviceId: "device"));

        return new SyncItemSnapshotRecord(
            item,
            new SyncObjectMetadata(
                BookmarkSyncState.Dirty,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                ContentHash: null,
                ModifiedDeviceId: "device"));
    }

    private static SyncItemDto CreateRemoteItem(
        string id,
        SystemTextSyncJsonSerializer serializer)
    {
        var item = new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            ParentId: null,
            SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: "Remote bookmark",
            Url: "https://example.com/",
            IsSecret: false,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: null,
            EncryptionNonce: null,
            CryptoProfileSecretGenerationId: null,
            SecretPayloadFormatVersion: null,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "remote-device",
            ContentHash: SyncRemoteObjectConstants.ContentHashPlaceholder);

        return SyncTestDtoHasher.WithContentHash(item, serializer);
    }

    private sealed class FakeSyncLocalStore : ISyncLocalStore
    {
        private readonly SyncLocalSnapshot _snapshot;

        public FakeSyncLocalStore(SyncLocalSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public List<SyncApplyBatch> AppliedBatches { get; } = [];

        public SyncLocalSnapshot LoadSnapshot()
        {
            return _snapshot;
        }

        public void ApplyRemoteChanges(SyncApplyBatch batch)
        {
            AppliedBatches.Add(batch);
        }

        public void MarkUploaded(
            SyncObjectIdentity identity,
            string? remoteEtag,
            string contentHash,
            DateTimeOffset syncedAtUtc)
        {
        }

        public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
        {
        }

        public void MarkQuarantinedRemoteObject(
            string objectKind,
            string relativePath,
            string? remoteEtag,
            string? contentHash,
            string reasonCode,
            DateTimeOffset seenAtUtc)
        {
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
