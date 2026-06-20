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

public sealed class SyncPushServiceTests
{
    [Fact]
    public async Task PushAsync_uploads_dirty_item_and_marks_it_clean()
    {
        var item = CreateLocalItem("bookmark", iconAssetId: null);
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items = [item]
        });
        var transport = new RecordingWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PushAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(1, summary.UploadedCount);
        Assert.Equal(0, summary.ConflictCount);
        var mark = Assert.Single(localStore.UploadedMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), mark.Identity);
        Assert.StartsWith("sha256:", mark.ContentHash, StringComparison.Ordinal);
        Assert.NotNull(mark.RemoteEtag);

        var remoteBytes = await transport.GetAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark")),
            CancellationToken.None);
        Assert.NotNull(remoteBytes);
        var remoteItem = serializer.Deserialize<SyncItemDto>(remoteBytes);
        Assert.Equal("bookmark", remoteItem.Id);
        Assert.Equal("Title", remoteItem.Title);
        Assert.Equal("https://example.com/", remoteItem.Url);
    }

    [Fact]
    public async Task PushAsync_marks_precondition_failure_as_conflict()
    {
        var item = CreateLocalItem("bookmark", iconAssetId: null);
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items = [item]
        });
        var transport = new RecordingWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark")),
            CorruptRemoteBytes,
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PushAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(0, summary.UploadedCount);
        Assert.Equal(1, summary.ConflictCount);
        Assert.Empty(localStore.UploadedMarks);
        var conflict = Assert.Single(localStore.ConflictMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), conflict.Identity);
        Assert.Equal("push-precondition-failed", conflict.ReasonCode);
    }

    [Fact]
    public async Task PushAsync_uploads_icon_before_item_that_references_it()
    {
        var icon = CreateIconAsset("icon");
        var item = CreateLocalItem("bookmark", iconAssetId: "icon");
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            IconAssets = [icon],
            Items = [item]
        });
        var transport = new RecordingWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PushAsync(CancellationToken.None);

        Assert.True(summary.Succeeded);
        Assert.Equal(2, summary.UploadedCount);
        Assert.Equal(
            [
                SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.IconAsset, "icon")),
                SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"))
            ],
            transport.PutPaths);
        Assert.Equal(2, localStore.UploadedMarks.Count);
    }

    [Fact]
    public async Task PushAsync_skips_item_when_referenced_icon_upload_conflicts()
    {
        var icon = CreateIconAsset("icon");
        var item = CreateLocalItem("bookmark", iconAssetId: "icon");
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            IconAssets = [icon],
            Items = [item]
        });
        var transport = new RecordingWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.IconAsset, "icon")),
            CorruptRemoteBytes,
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PushAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(0, summary.UploadedCount);
        Assert.Equal(1, summary.ConflictCount);
        Assert.Equal(0, summary.ErrorCount);
        var conflict = Assert.Single(localStore.ConflictMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.IconAsset, "icon"), conflict.Identity);
        Assert.DoesNotContain(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark")),
            transport.PutPaths);
    }

    [Fact]
    public async Task PushAsync_skips_child_item_when_parent_folder_upload_conflicts()
    {
        var folder = CreateLocalFolder("folder", parentId: null);
        var bookmark = CreateLocalItem("bookmark", iconAssetId: null, parentId: "folder");
        var localStore = new FakeSyncLocalStore(EmptySnapshot() with
        {
            Items = [folder, bookmark]
        });
        var transport = new RecordingWebDavSyncTransport();
        var serializer = new SystemTextSyncJsonSerializer();
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "folder")),
            CorruptRemoteBytes,
            expectedEtag: null,
            createOnly: true,
            CancellationToken.None);
        var service = CreateService(localStore, transport, serializer);

        var summary = await service.PushAsync(CancellationToken.None);

        Assert.False(summary.Succeeded);
        Assert.Equal(0, summary.UploadedCount);
        Assert.Equal(1, summary.ConflictCount);
        var conflict = Assert.Single(localStore.ConflictMarks);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "folder"), conflict.Identity);
        Assert.DoesNotContain(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark")),
            transport.PutPaths);
    }

    private static SyncPushService CreateService(
        FakeSyncLocalStore localStore,
        RecordingWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer)
    {
        return new SyncPushService(
            localStore,
            transport,
            serializer,
            new SyncRemoteDtoMapper(serializer, new Sha256SyncContentHasher()),
            clock: () => Now,
            log: _ => { });
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
        string? iconAssetId,
        string? parentId = null)
    {
        var item = new BookmarkItemRecord(
            id,
            ParentId: parentId,
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
                ModifiedDeviceId: "device"),
            IconAssetId: iconAssetId);

        return new SyncItemSnapshotRecord(item, CreateMetadata());
    }

    private static SyncItemSnapshotRecord CreateLocalFolder(
        string id,
        string? parentId)
    {
        var item = new BookmarkItemRecord(
            id,
            ParentId: parentId,
            Kind: BookmarkItemKind.Folder,
            SortOrder: 1000,
            Title: "Folder",
            Url: null,
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
                ModifiedDeviceId: "device"),
            IconAssetId: null);

        return new SyncItemSnapshotRecord(item, CreateMetadata());
    }

    private static SyncIconAssetSnapshotRecord CreateIconAsset(string id)
    {
        return new SyncIconAssetSnapshotRecord(
            new BookmarkIconAssetRecord(
                id,
                "sha256",
                "source-hash",
                SourceSizeBytes: 3,
                ProcessedMimeType: "image/png",
                ProcessedWidth: 32,
                ProcessedHeight: 32,
                ProcessedBytes: ProcessedIconBytes,
                CreatedAtUtc: Now),
            CreateMetadata());
    }

    private static SyncObjectMetadata CreateMetadata()
    {
        return new SyncObjectMetadata(
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ContentHash: null,
            ModifiedDeviceId: "device");
    }

    private sealed class FakeSyncLocalStore : ISyncLocalStore
    {
        private readonly SyncLocalSnapshot _snapshot;

        public FakeSyncLocalStore(SyncLocalSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public List<UploadedMark> UploadedMarks { get; } = [];

        public List<ConflictMark> ConflictMarks { get; } = [];

        public SyncLocalSnapshot LoadSnapshot()
        {
            return _snapshot;
        }

        public void ApplyRemoteChanges(SyncApplyBatch batch)
        {
        }

        public void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc)
        {
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

        public void ClearQuarantinedRemoteObject(string id)
        {
        }
    }

    private sealed class RecordingWebDavSyncTransport : InMemoryWebDavSyncTransport
    {
        public List<string> PutPaths { get; } = [];

        public override async Task<SyncPutResult> PutAsync(
            string relativePath,
            byte[] bytes,
            string? expectedEtag,
            bool createOnly,
            CancellationToken cancellationToken)
        {
            PutPaths.Add(relativePath);
            return await base.PutAsync(relativePath, bytes, expectedEtag, createOnly, cancellationToken)
                .ConfigureAwait(false);
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

    private static readonly byte[] ProcessedIconBytes = [1, 2, 3];

    private static readonly byte[] CorruptRemoteBytes = [1, 2, 3];

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
