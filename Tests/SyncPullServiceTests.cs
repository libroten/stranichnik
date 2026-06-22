using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncPullServiceTests
{
    [Fact]
    public async Task PullAsync_applies_remote_item_to_local_store()
    {
        var localStore = new FakeSyncLocalStore(EmptySnapshot());
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var remoteItem = CreateRemoteItem("remote-bookmark", serializer);
        await PutRemoteObjectAsync(transport, serializer, SyncObjectKind.Item, remoteItem.Id, remoteItem);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(1, summary.DownloadedCount);
        var item = Assert.Single(localStore.AppliedBatches).Items.Single();
        Assert.Equal("remote-bookmark", item.Value.Id);
        Assert.Empty(localStore.UploadedMarks);
        Assert.Empty(localStore.ConflictMarks);
        Assert.Empty(localStore.QuarantinedObjects);
    }

    [Fact]
    public async Task PullAsync_refreshes_dirty_same_content_object_without_marking_uploaded()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var remoteItem = CreateRemoteItem("bookmark", serializer);
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Dirty,
                    remoteItem.ContentHash)
            ]
        });
        var transport = new InMemoryWebDavSyncTransport();
        await PutRemoteObjectAsync(transport, serializer, SyncObjectKind.Item, remoteItem.Id, remoteItem);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(0, summary.DownloadedCount);
        Assert.Empty(localStore.UploadedMarks);
        var mark = Assert.Single(localStore.RefreshedDirtyMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), mark.Identity);
        Assert.Equal(remoteItem.ContentHash, mark.ContentHash);
        Assert.Empty(localStore.AppliedBatches.Single().Items);
    }

    [Fact]
    public async Task PullAsync_reports_zero_downloaded_when_remote_object_is_already_current_locally()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var remoteItem = CreateRemoteItem("bookmark", serializer);
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Clean,
                    remoteItem.ContentHash,
                    hasBeenSynced: true)
            ]
        });
        var transport = new InMemoryWebDavSyncTransport();
        await PutRemoteObjectAsync(transport, serializer, SyncObjectKind.Item, remoteItem.Id, remoteItem);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(0, summary.DownloadedCount);
        Assert.Empty(localStore.UploadedMarks);
        Assert.Empty(localStore.AppliedBatches.Single().Items);
    }

    [Fact]
    public async Task PullAsync_marks_clean_synced_local_item_missing_from_remote_as_dirty()
    {
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Clean,
                    "sha256:local",
                    hasBeenSynced: true)
            ]
        });
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(0, summary.DownloadedCount);
        var mark = Assert.Single(localStore.DirtyMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), mark);
        Assert.Empty(localStore.AppliedBatches.Single().Items);
    }

    [Fact]
    public async Task PullAsync_quarantines_invalid_remote_object()
    {
        var localStore = new FakeSyncLocalStore(EmptySnapshot());
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            "items/broken.json",
            [1, 2, 3],
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(0, summary.DownloadedCount);
        Assert.Equal(1, summary.InvalidRemoteObjectCount);
        var quarantine = Assert.Single(localStore.QuarantinedObjects);
        Assert.Equal("items/broken.json", quarantine.RelativePath);
        Assert.Equal("invalid-json", quarantine.ReasonCode);
    }

    [Fact]
    public async Task PullAsync_does_not_fail_for_known_unchanged_invalid_remote_object()
    {
        var invalidBytes = new byte[] { 1, 2, 3 };
        var rawContentHash = new Sha256SyncContentHasher().ComputeHash(invalidBytes);
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                new SyncQuarantinedRemoteObjectRecord(
                    "items/broken.json",
                    SyncObjectKind.Item.ToString(),
                    "items/broken.json",
                    "\"memory-1\"",
                    rawContentHash,
                    ReasonCode: "invalid-json",
                    FirstSeenAtUtc: Now,
                    LastSeenAtUtc: Now,
                    SeenCount: 1)
            ]
        });
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            "items/broken.json",
            invalidBytes,
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(1, summary.InvalidRemoteObjectCount);
        var quarantine = Assert.Single(localStore.QuarantinedObjects);
        Assert.Equal("items/broken.json", quarantine.RelativePath);
        Assert.Empty(localStore.ClearedQuarantinedObjectIds);
    }

    [Fact]
    public async Task PullAsync_marks_dirty_changed_object_as_conflict()
    {
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("bookmark", BookmarkSyncState.Dirty, "sha256:local")
            ]
        });
        var transport = new InMemoryWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var remoteItem = CreateRemoteItem("bookmark", serializer);
        await PutRemoteObjectAsync(transport, serializer, SyncObjectKind.Item, remoteItem.Id, remoteItem);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PullAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(1, summary.ConflictCount);
        var conflict = Assert.Single(localStore.ConflictMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), conflict.Identity);
        Assert.Equal("local-dirty-remote-changed", conflict.ReasonCode);
        Assert.Empty(localStore.AppliedBatches.Single().Items);
    }

    private static SyncPullService CreateService(
        FakeSyncLocalStore localStore,
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer)
    {
        return new SyncPullService(
            localStore,
            new SyncRemoteObjectReader(transport, serializer, log: _ => { }),
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

    private static SyncItemSnapshotRecord CreateLocalItem(
        string id,
        BookmarkSyncState syncState,
        string? contentHash,
        bool hasBeenSynced = false)
    {
        var lastSyncedAtUtc = hasBeenSynced ? Now : (DateTimeOffset?)null;
        var item = new BookmarkItemRecord(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            Title: "Title",
            Url: "https://example.com/",
            IsSecret: false,
            EncryptedPayload: null,
            SecretGenerationId: null,
            new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                syncState,
                RemoteEtag: null,
                LastSyncedAtUtc: lastSyncedAtUtc,
                ContentHash: contentHash,
                ModifiedDeviceId: "device"));

        return new SyncItemSnapshotRecord(
            item,
            new SyncObjectMetadata(
                syncState,
                RemoteEtag: null,
                LastSyncedAtUtc: lastSyncedAtUtc,
                contentHash,
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

        public List<UploadedMark> UploadedMarks { get; } = [];

        public List<UploadedMark> RefreshedDirtyMarks { get; } = [];

        public List<ConflictMark> ConflictMarks { get; } = [];

        public List<SyncObjectIdentity> DirtyMarks { get; } = [];

        public List<QuarantineMark> QuarantinedObjects { get; } = [];

        public List<string> ClearedQuarantinedObjectIds { get; } = [];

        public SyncLocalSnapshot LoadSnapshot()
        {
            return _snapshot;
        }

        public void ApplyRemoteChanges(SyncApplyBatch batch)
        {
            AppliedBatches.Add(batch);
        }

        public void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc)
        {
            ApplyRemoteChanges(plan.ApplyBatch);

            foreach (var matchedObject in plan.MatchedDirtyObjects)
            {
                RefreshedDirtyMarks.Add(new UploadedMark(
                    matchedObject.Identity,
                    matchedObject.RemoteEtag,
                    matchedObject.ContentHash,
                    syncedAtUtc));
            }

            foreach (var conflict in plan.Conflicts)
                MarkConflict(conflict.Identity, conflict.ReasonCode);

            foreach (var quarantineCandidate in plan.QuarantinedRemoteObjects)
            {
                MarkQuarantinedRemoteObject(
                    quarantineCandidate.ObjectKind,
                    quarantineCandidate.RelativePath,
                    quarantineCandidate.RemoteEtag,
                    quarantineCandidate.ContentHash,
                    quarantineCandidate.ReasonCode,
                    syncedAtUtc);
            }

            foreach (var quarantineCandidate in plan.KnownQuarantinedRemoteObjects)
            {
                MarkQuarantinedRemoteObject(
                    quarantineCandidate.ObjectKind,
                    quarantineCandidate.RelativePath,
                    quarantineCandidate.RemoteEtag,
                    quarantineCandidate.ContentHash,
                    quarantineCandidate.ReasonCode,
                    syncedAtUtc);
            }

            foreach (var resolvedQuarantineId in plan.ResolvedQuarantinedRemoteObjectIds.Distinct(StringComparer.Ordinal))
                ClearQuarantinedRemoteObject(resolvedQuarantineId);

            foreach (var missingRemoteObject in plan.MissingRemoteObjects)
                MarkDirty(missingRemoteObject);
        }

        public void MarkUploaded(
            SyncObjectIdentity identity,
            string? remoteEtag,
            string contentHash,
            DateTimeOffset syncedAtUtc)
        {
            UploadedMarks.Add(new UploadedMark(identity, remoteEtag, contentHash, syncedAtUtc));
        }

        public void MarkConflict(SyncObjectIdentity identity, string reasonCode)
        {
            ConflictMarks.Add(new ConflictMark(identity, reasonCode));
        }

        public void MarkDirty(SyncObjectIdentity identity)
        {
            DirtyMarks.Add(identity);
        }

        public void MarkQuarantinedRemoteObject(
            string objectKind,
            string relativePath,
            string? remoteEtag,
            string? contentHash,
            string reasonCode,
            DateTimeOffset seenAtUtc)
        {
            QuarantinedObjects.Add(new QuarantineMark(
                objectKind,
                relativePath,
                remoteEtag,
                contentHash,
                reasonCode,
                seenAtUtc));
        }

        public void ClearQuarantinedRemoteObject(string id)
        {
            ClearedQuarantinedObjectIds.Add(id);
        }
    }

    private sealed record UploadedMark(
        SyncObjectIdentity Identity,
        string? RemoteEtag,
        string ContentHash,
        DateTimeOffset SyncedAtUtc);

    private sealed record ConflictMark(
        SyncObjectIdentity Identity,
        string ReasonCode);

    private sealed record QuarantineMark(
        string ObjectKind,
        string RelativePath,
        string? RemoteEtag,
        string? ContentHash,
        string ReasonCode,
        DateTimeOffset SeenAtUtc);

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
