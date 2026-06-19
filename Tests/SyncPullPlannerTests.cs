using System;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncPullPlannerTests
{
    [Fact]
    public void Plan_applies_missing_remote_item()
    {
        var remoteItem = CreateRemoteItem("remote", "sha256:remote");

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, remoteItem.Id, remoteItem)]);

        var item = Assert.Single(plan.ApplyBatch.Items);
        Assert.Equal("remote", item.Value.Id);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "remote"), item.Identity);
        Assert.Empty(plan.Conflicts);
        Assert.Empty(plan.QuarantinedRemoteObjects);
    }

    [Fact]
    public void Plan_applies_clean_item_when_remote_content_changed()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("bookmark", BookmarkSyncState.Clean, "sha256:old")
            ]
        };
        var remoteItem = CreateRemoteItem("bookmark", "sha256:new");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, remoteItem.Id, remoteItem)]);

        var item = Assert.Single(plan.ApplyBatch.Items);
        Assert.Equal("sha256:new", item.Value.ContentHash);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Plan_marks_dirty_item_as_matched_when_remote_content_is_same()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("bookmark", BookmarkSyncState.Dirty, "sha256:same")
            ]
        };
        var remoteItem = CreateRemoteItem("bookmark", "sha256:same");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, remoteItem.Id, remoteItem)]);

        Assert.Empty(plan.ApplyBatch.Items);
        var match = Assert.Single(plan.MatchedDirtyObjects);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), match.Identity);
        Assert.Equal("sha256:same", match.ContentHash);
    }

    [Fact]
    public void Plan_marks_dirty_item_as_conflict_when_remote_content_changed()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("bookmark", BookmarkSyncState.Dirty, "sha256:local")
            ]
        };
        var remoteItem = CreateRemoteItem("bookmark", "sha256:remote");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, remoteItem.Id, remoteItem)]);

        Assert.Empty(plan.ApplyBatch.Items);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), conflict.Identity);
        Assert.Equal("sha256:local", conflict.LocalContentHash);
        Assert.Equal("sha256:remote", conflict.RemoteContentHash);
    }

    [Fact]
    public void Plan_quarantines_failed_remote_read()
    {
        var remoteInfo = new SyncRemoteObjectInfo("items/broken.json", "etag", null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "broken"));

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [],
            [],
            [],
            [],
            [failedRead]);

        Assert.Empty(plan.ApplyBatch.Items);
        var quarantine = Assert.Single(plan.QuarantinedRemoteObjects);
        Assert.Equal("items/broken.json", quarantine.RelativePath);
        Assert.Equal("invalid-json", quarantine.ReasonCode);
    }

    [Fact]
    public void Plan_treats_unchanged_quarantine_as_known_problem()
    {
        var snapshot = EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                CreateQuarantine(
                    "items/broken.json",
                    "etag",
                    "invalid-json")
            ]
        };
        var remoteInfo = new SyncRemoteObjectInfo("items/broken.json", "etag", null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "broken"));

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [failedRead]);

        Assert.Empty(plan.QuarantinedRemoteObjects);
        var knownProblem = Assert.Single(plan.KnownQuarantinedRemoteObjects);
        Assert.Equal("items/broken.json", knownProblem.RelativePath);
        Assert.Empty(plan.ResolvedQuarantinedRemoteObjectIds);
    }

    [Fact]
    public void Plan_treats_changed_quarantine_as_new_problem()
    {
        var snapshot = EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                CreateQuarantine(
                    "items/broken.json",
                    "old-etag",
                    "invalid-json")
            ]
        };
        var remoteInfo = new SyncRemoteObjectInfo("items/broken.json", "new-etag", null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "broken"));

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [failedRead]);

        Assert.Empty(plan.KnownQuarantinedRemoteObjects);
        var quarantine = Assert.Single(plan.QuarantinedRemoteObjects);
        Assert.Equal("items/broken.json", quarantine.RelativePath);
    }

    [Fact]
    public void Plan_resolves_quarantine_when_remote_object_becomes_valid()
    {
        var snapshot = EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                CreateQuarantine(
                    "items/bookmark.json",
                    "old-etag",
                    "invalid-json")
            ]
        };
        var remoteItem = CreateRemoteItem("bookmark", "sha256:remote");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, "bookmark", remoteItem)]);

        Assert.Empty(plan.QuarantinedRemoteObjects);
        Assert.Empty(plan.KnownQuarantinedRemoteObjects);
        Assert.Contains("items/bookmark.json", plan.ResolvedQuarantinedRemoteObjectIds);
    }

    [Fact]
    public void Plan_quarantines_remote_object_when_path_identity_does_not_match_payload()
    {
        var remoteItem = CreateRemoteItem("payload-id", "sha256:remote");

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, "path-id", remoteItem)]);

        Assert.Empty(plan.ApplyBatch.Items);
        var quarantine = Assert.Single(plan.QuarantinedRemoteObjects);
        Assert.Equal("identity-mismatch", quarantine.ReasonCode);
        Assert.Equal("sha256:remote", quarantine.ContentHash);
    }

    [Fact]
    public void Plan_skips_existing_secret_reset_event()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                new SecretResetEventRecord(
                    "local-reset",
                    "generation",
                    Now,
                    "device",
                    BookmarkSyncState.Clean,
                    "etag",
                    Now)
            ]
        };
        var remoteReset = new SyncSecretResetEventDto(
            SyncRemoteObjectConstants.SecretResetEventSchema,
            SyncRemoteObjectConstants.FormatVersion,
            "generation",
            Now,
            "device",
            1,
            Now,
            Now,
            "device",
            "sha256:reset");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [Success(SyncObjectKind.SecretResetEvent, "generation", remoteReset)],
            [],
            [],
            [],
            []);

        Assert.Empty(plan.ApplyBatch.SecretResetEvents);
    }

    [Fact]
    public void Plan_skips_crypto_profile_for_reset_generation()
    {
        var resetEvent = CreateRemoteResetEvent("generation");
        var profile = CreateRemoteCryptoProfile("generation");

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [Success(SyncObjectKind.SecretResetEvent, "generation", resetEvent)],
            [Success(SyncObjectKind.CryptoProfile, "generation", profile)],
            [],
            [],
            []);

        Assert.Single(plan.ApplyBatch.SecretResetEvents);
        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
    }

    [Fact]
    public void Plan_skips_secret_item_for_existing_reset_generation()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                new SecretResetEventRecord(
                    "local-reset",
                    "generation",
                    Now,
                    "device",
                    BookmarkSyncState.Clean,
                    "etag",
                    Now)
            ]
        };
        var secretItem = CreateRemoteSecretItem("secret", "generation");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [Success(SyncObjectKind.Item, "secret", secretItem)]);

        Assert.Empty(plan.ApplyBatch.Items);
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
        string? contentHash)
    {
        var item = new BookmarkItemRecord(
            id,
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            Title: "Title",
            Url: "https://example.com/",
            IsSecret: false,
            EncryptedPayload: null,
            new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                syncState,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                ContentHash: null,
                ModifiedDeviceId: "device"));

        return new SyncItemSnapshotRecord(
            item,
            new SyncObjectMetadata(
                syncState,
                RemoteEtag: null,
                LastSyncedAtUtc: null,
                contentHash,
            ModifiedDeviceId: "device"));
    }

    private static SyncQuarantinedRemoteObjectRecord CreateQuarantine(
        string relativePath,
        string remoteEtag,
        string reasonCode)
    {
        return new SyncQuarantinedRemoteObjectRecord(
            relativePath,
            SyncObjectKind.Item.ToString(),
            relativePath,
            remoteEtag,
            ContentHash: null,
            ReasonCode: reasonCode,
            FirstSeenAtUtc: Now,
            LastSeenAtUtc: Now,
            SeenCount: 1);
    }

    private static SyncRemoteReadResult<T> Success<T>(
        SyncObjectKind kind,
        string id,
        T value)
    {
        return SyncRemoteReadResult.Success(
            new SyncRemoteObjectInfo(
                SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(kind, id)),
                "etag",
                null,
                null),
            new SyncObjectIdentity(kind, id),
            value);
    }

    private static SyncItemDto CreateRemoteItem(string id, string contentHash)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            ParentId: null,
            SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: "Title",
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
            ModifiedDeviceId: "device",
            contentHash);
    }

    private static SyncItemDto CreateRemoteSecretItem(
        string id,
        string secretGenerationId)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            ParentId: null,
            SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: null,
            Url: null,
            IsSecret: true,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: Convert.ToBase64String([1, 2, 3]),
            EncryptionNonce: Convert.ToBase64String([4, 5, 6]),
            CryptoProfileSecretGenerationId: secretGenerationId,
            SecretPayloadFormatVersion: 1,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:secret");
    }

    private static SyncCryptoProfileDto CreateRemoteCryptoProfile(string secretGenerationId)
    {
        return new SyncCryptoProfileDto(
            SyncRemoteObjectConstants.CryptoProfileSchema,
            SyncRemoteObjectConstants.FormatVersion,
            secretGenerationId,
            ProfileVersion: 1,
            KdfAlgorithm: "pbkdf2",
            KdfIterations: 10,
            Salt: Convert.ToBase64String([1, 2, 3]),
            EncryptedDataKey: Convert.ToBase64String([4, 5, 6]),
            DataKeyNonce: Convert.ToBase64String([7, 8, 9]),
            PasswordCheckPayload: Convert.ToBase64String([10, 11, 12]),
            PasswordCheckNonce: Convert.ToBase64String([13, 14, 15]),
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:profile");
    }

    private static SyncSecretResetEventDto CreateRemoteResetEvent(string secretGenerationId)
    {
        return new SyncSecretResetEventDto(
            SyncRemoteObjectConstants.SecretResetEventSchema,
            SyncRemoteObjectConstants.FormatVersion,
            secretGenerationId,
            Now,
            "device",
            ResetEventFormatVersion: 1,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:reset");
    }

    private static readonly DateTimeOffset Now = new(2026, 6, 14, 0, 0, 0, TimeSpan.Zero);
}
