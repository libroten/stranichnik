using System;
using Stranichnik.Security;
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
    public void Plan_marks_clean_synced_local_item_missing_from_remote_as_dirty()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Clean,
                    "sha256:local",
                    hasBeenSynced: true)
            ]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            []);

        var missing = Assert.Single(plan.MissingRemoteObjects);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), missing);
        Assert.Empty(plan.ApplyBatch.Items);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Plan_does_not_mark_clean_never_synced_local_item_as_missing_remote()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Clean,
                    "sha256:local")
            ]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            []);

        Assert.DoesNotContain(
            new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"),
            plan.MissingRemoteObjects);
    }

    [Fact]
    public void Plan_does_not_mark_existing_invalid_remote_item_as_missing_remote()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem(
                    "bookmark",
                    BookmarkSyncState.Clean,
                    "sha256:local",
                    hasBeenSynced: true)
            ]
        };
        var remoteInfo = new SyncRemoteObjectInfo("items/bookmark.json", "etag", null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"));

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            [failedRead]);

        Assert.DoesNotContain(
            new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"),
            plan.MissingRemoteObjects);
        var quarantine = Assert.Single(plan.QuarantinedRemoteObjects);
        Assert.Equal("items/bookmark.json", quarantine.RelativePath);
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
    public void Plan_marks_conflicted_item_as_matched_when_remote_content_is_same()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalItem("bookmark", BookmarkSyncState.Conflict, "sha256:same")
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
        Assert.Empty(plan.Conflicts);
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
    public void Plan_treats_unchanged_quarantine_with_content_hash_as_known_problem_without_etag()
    {
        var snapshot = EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                CreateQuarantine(
                    "items/broken.json",
                    remoteEtag: null,
                    "invalid-json",
                    contentHash: "sha256:raw")
            ]
        };
        var remoteInfo = new SyncRemoteObjectInfo("items/broken.json", null, null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "broken"),
            contentHash: "sha256:raw");

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
    }

    [Fact]
    public void Plan_treats_changed_quarantine_content_hash_as_new_problem_without_etag()
    {
        var snapshot = EmptySnapshot() with
        {
            QuarantinedRemoteObjects =
            [
                CreateQuarantine(
                    "items/broken.json",
                    remoteEtag: null,
                    "invalid-json",
                    contentHash: "sha256:old")
            ]
        };
        var remoteInfo = new SyncRemoteObjectInfo("items/broken.json", null, null, null);
        var failedRead = SyncRemoteReadResult.Failed<SyncItemDto>(
            SyncRemoteReadStatus.InvalidJson,
            remoteInfo,
            new SyncObjectIdentity(SyncObjectKind.Item, "broken"),
            contentHash: "sha256:new");

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
    public void Plan_marks_dirty_existing_secret_reset_event_as_satisfied()
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
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null)
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
        var satisfied = Assert.Single(plan.SatisfiedResetEvents);
        Assert.Equal("generation", satisfied.SecretGenerationId);
        Assert.Equal("etag", satisfied.RemoteEtag);
    }

    [Fact]
    public void Plan_applies_only_latest_crypto_profile_when_local_profile_is_missing()
    {
        var oldProfile = CreateRemoteCryptoProfile("old-generation") with
        {
            UpdatedAtUtc = Now.AddMinutes(-1),
            ContentHash = "sha256:old-profile"
        };
        var newProfile = CreateRemoteCryptoProfile("new-generation") with
        {
            UpdatedAtUtc = Now.AddMinutes(1),
            ContentHash = "sha256:new-profile"
        };

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [],
            [
                Success(SyncObjectKind.CryptoProfile, "old-generation", oldProfile),
                Success(SyncObjectKind.CryptoProfile, "new-generation", newProfile)
            ],
            [],
            [],
            []);

        var profile = Assert.Single(plan.ApplyBatch.CryptoProfiles);
        Assert.Equal("new-generation", profile.Value.SecretGenerationId);
    }

    [Fact]
    public void Plan_does_not_apply_remote_crypto_profile_over_different_local_generation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles = [CreateLocalProfile("local-generation")]
        };
        var remoteProfile = CreateRemoteCryptoProfile("remote-generation");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "remote-generation", remoteProfile)],
            [],
            [],
            []);

        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
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
    public void Plan_requires_secret_reset_confirmation_when_remote_reset_affects_local_secret_data()
    {
        var profile = CreateLocalProfile("generation");
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalSecretItem(
                    "secret",
                    profile.Profile.Id,
                    BookmarkSyncState.Dirty,
                    "sha256:secret")
            ],
            CryptoProfiles = [profile]
        };
        var resetEvent = CreateRemoteResetEvent("generation");

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [Success(SyncObjectKind.SecretResetEvent, "generation", resetEvent)],
            [],
            [],
            [],
            []);

        Assert.NotNull(plan.SecretConflictConfirmation);
        Assert.Equal(
            SyncSecretConflictConfirmationReason.RemoteSecretReset,
            plan.SecretConflictConfirmation!.Reason);
    }

    [Fact]
    public void Plan_does_not_require_secret_reset_confirmation_when_generation_has_no_local_secret_data()
    {
        var resetEvent = CreateRemoteResetEvent("generation");

        var plan = SyncPullPlanner.Plan(
            EmptySnapshot(),
            [Success(SyncObjectKind.SecretResetEvent, "generation", resetEvent)],
            [],
            [],
            [],
            []);

        Assert.Null(plan.SecretConflictConfirmation);
    }

    [Fact]
    public void Plan_marks_dirty_crypto_profile_as_matched_when_remote_content_is_same()
    {
        var localProfile = CreateLocalProfile("generation") with
        {
            SyncMetadata = new SyncObjectMetadata(
                BookmarkSyncState.Dirty,
                RemoteEtag: "profile-etag",
                LastSyncedAtUtc: Now,
                ContentHash: "sha256:profile",
                ModifiedDeviceId: "device")
        };
        var remoteProfile = CreateRemoteCryptoProfile("generation");
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles = [localProfile]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "generation", remoteProfile)],
            [],
            [],
            []);

        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
        Assert.Empty(plan.Conflicts);
        var match = Assert.Single(plan.MatchedDirtyObjects);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.CryptoProfile, "generation"), match.Identity);
        Assert.Equal("sha256:profile", match.ContentHash);
    }

    [Fact]
    public void Plan_skips_secret_objects_for_conflicted_crypto_profile_generation()
    {
        var localProfile = CreateLocalProfile("generation") with
        {
            SyncMetadata = new SyncObjectMetadata(
                BookmarkSyncState.Dirty,
                RemoteEtag: "profile-etag",
                LastSyncedAtUtc: Now,
                ContentHash: "sha256:local-profile",
                ModifiedDeviceId: "device")
        };
        var remoteProfile = CreateRemoteCryptoProfile("generation") with
        {
            ContentHash = "sha256:remote-profile"
        };
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles = [localProfile]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "generation", remoteProfile)],
            [],
            [Success(SyncObjectKind.SecretIconAsset, "secret-icon", CreateRemoteSecretIconAsset("secret-icon", "generation"))],
            [Success(SyncObjectKind.Item, "secret", CreateRemoteSecretItem("secret", "generation"))]);

        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
        Assert.Empty(plan.ApplyBatch.SecretIconAssets);
        Assert.Empty(plan.ApplyBatch.Items);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.CryptoProfile, "generation"), conflict.Identity);
        Assert.NotNull(plan.SecretConflictConfirmation);
        Assert.Equal(
            SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange,
            plan.SecretConflictConfirmation!.Reason);
    }

    [Fact]
    public void Plan_requires_password_change_confirmation_for_newer_remote_profile_from_another_generation()
    {
        var localProfile = CreateLocalProfile("local-generation");
        var remoteProfile = CreateRemoteCryptoProfile("remote-generation") with
        {
            UpdatedAtUtc = Now.AddMinutes(1),
            ContentHash = "sha256:remote-profile"
        };
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles = [localProfile]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "remote-generation", remoteProfile)],
            [],
            [],
            []);

        Assert.NotNull(plan.SecretConflictConfirmation);
        Assert.Equal(
            SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange,
            plan.SecretConflictConfirmation!.Reason);
        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
    }

    [Fact]
    public void Plan_requires_password_change_confirmation_when_local_reset_baseline_is_stale()
    {
        var remoteProfile = CreateRemoteCryptoProfile("generation") with
        {
            ContentHash = "sha256:remote-new-profile"
        };
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                new SecretResetEventRecord(
                    "local-reset",
                    "generation",
                    Now,
                    "device",
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    BaselineCryptoProfileContentHash: "sha256:old-profile",
                    BaselineCryptoProfileRemoteEtag: "old-profile-etag")
            ]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "generation", remoteProfile)],
            [],
            [],
            []);

        Assert.NotNull(plan.SecretConflictConfirmation);
        Assert.Equal(
            SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange,
            plan.SecretConflictConfirmation!.Reason);
    }

    [Fact]
    public void Plan_allows_local_reset_when_remote_profile_matches_reset_baseline()
    {
        var remoteProfile = CreateRemoteCryptoProfile("generation") with
        {
            ContentHash = "sha256:old-profile"
        };
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                new SecretResetEventRecord(
                    "local-reset",
                    "generation",
                    Now,
                    "device",
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    BaselineCryptoProfileContentHash: "sha256:old-profile",
                    BaselineCryptoProfileRemoteEtag: "old-profile-etag")
            ]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [Success(SyncObjectKind.CryptoProfile, "generation", remoteProfile)],
            [],
            [],
            []);

        Assert.Null(plan.SecretConflictConfirmation);
    }

    [Fact]
    public void Plan_allows_local_reset_when_remote_reset_already_exists_even_if_old_profile_differs()
    {
        var remoteReset = CreateRemoteResetEvent("generation");
        var remoteProfile = CreateRemoteCryptoProfile("generation") with
        {
            ContentHash = "sha256:remote-new-profile"
        };
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                new SecretResetEventRecord(
                    "local-reset",
                    "generation",
                    Now,
                    "device",
                    BookmarkSyncState.Dirty,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    BaselineCryptoProfileContentHash: "sha256:old-profile",
                    BaselineCryptoProfileRemoteEtag: "old-profile-etag")
            ]
        };

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [Success(SyncObjectKind.SecretResetEvent, "generation", remoteReset)],
            [Success(SyncObjectKind.CryptoProfile, "generation", remoteProfile)],
            [],
            [],
            []);

        Assert.Null(plan.SecretConflictConfirmation);
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

    [Fact]
    public void Plan_does_not_mark_local_secret_item_for_reset_generation_as_missing_remote()
    {
        var profile = CreateLocalProfile("generation");
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateLocalSecretItem(
                    "secret",
                    profile.Profile.Id,
                    BookmarkSyncState.Clean,
                    "sha256:secret",
                    hasBeenSynced: true)
            ],
            CryptoProfiles = [profile],
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

        var plan = SyncPullPlanner.Plan(
            snapshot,
            [],
            [],
            [],
            [],
            []);

        Assert.DoesNotContain(
            new SyncObjectIdentity(SyncObjectKind.Item, "secret"),
            plan.MissingRemoteObjects);
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
                ContentHash: null,
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

    private static SyncItemSnapshotRecord CreateLocalSecretItem(
        string id,
        long cryptoProfileId,
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
            Title: null,
            Url: null,
            IsSecret: true,
            new EncryptedBookmarkPayloadRecord(
                Payload: new byte[] { 1, 2, 3 },
                Nonce: new byte[] { 4, 5, 6 },
                CryptoProfileId: cryptoProfileId),
            SecretGenerationId: "generation",
            new BookmarkItemMetadata(
                Now,
                Now,
                DeletedAtUtc: null,
                Revision: 1,
                syncState,
                RemoteEtag: null,
                LastSyncedAtUtc: lastSyncedAtUtc,
                ContentHash: null,
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

    private static SyncCryptoProfileSnapshotRecord CreateLocalProfile(string secretGenerationId)
    {
        var profile = new CryptoProfileRecord(
            Id: 100,
            ProfileVersion: 1,
            KdfName: "pbkdf2",
            KdfHashAlgorithm: "sha256",
            KdfIterations: 10,
            KdfSalt: new byte[] { 1, 2, 3 },
            KekLengthBytes: 32,
            DataKeyAlgorithm: "aes",
            WrappedDataKey: new byte[] { 4, 5, 6 },
            WrappedDataKeyNonce: new byte[] { 7, 8, 9 },
            EncryptionAlgorithm: "aes",
            PayloadFormat: "json",
            PasswordCheckPayload: new byte[] { 10, 11, 12 },
            PasswordCheckNonce: new byte[] { 13, 14, 15 },
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            SecretGenerationId: secretGenerationId);

        return new SyncCryptoProfileSnapshotRecord(
            profile,
            new SyncObjectMetadata(
                BookmarkSyncState.Clean,
                RemoteEtag: "profile-etag",
                LastSyncedAtUtc: Now,
                ContentHash: "sha256:profile",
                ModifiedDeviceId: "device"));
    }

    private static SyncQuarantinedRemoteObjectRecord CreateQuarantine(
        string relativePath,
        string? remoteEtag,
        string reasonCode,
        string? contentHash = null)
    {
        return new SyncQuarantinedRemoteObjectRecord(
            relativePath,
            SyncObjectKind.Item.ToString(),
            relativePath,
            remoteEtag,
            contentHash,
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

    private static SyncSecretIconAssetDto CreateRemoteSecretIconAsset(
        string id,
        string secretGenerationId)
    {
        return new SyncSecretIconAssetDto(
            SyncRemoteObjectConstants.SecretIconAssetSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            "sha256",
            "remote-secret-source-hash",
            SourceSizeBytes: 3,
            ProcessedMimeType: "image/png",
            ProcessedWidth: 32,
            ProcessedHeight: 32,
            EncryptedProcessedBytes: Convert.ToBase64String([1, 2, 3]),
            EncryptionNonce: Convert.ToBase64String([4, 5, 6]),
            PayloadFormatVersion: 1,
            SecretGenerationId: secretGenerationId,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:secret-icon");
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
