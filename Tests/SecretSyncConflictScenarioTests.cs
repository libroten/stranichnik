using System;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Push;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretSyncConflictScenarioTests
{
    [Fact]
    public void Case1_one_sided_password_change_applies_remote_profile_without_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(OldGeneration, contentHash: "sha256:p0-profile")
            ]
        };
        var remoteProfile = CreateRemoteProfile(
            OldGeneration,
            contentHash: "sha256:pa-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(snapshot, cryptoProfiles: [remoteProfile]);

        Assert.Null(plan.SecretConflictConfirmation);
        Assert.Empty(plan.Conflicts);
        var profile = Assert.Single(plan.ApplyBatch.CryptoProfiles);
        Assert.Equal(OldGeneration, profile.Value.SecretGenerationId);
        Assert.Equal("sha256:pa-profile", profile.Value.ContentHash);
    }

    [Fact]
    public void Case2_one_sided_reset_only_requires_remote_reset_confirmation_on_other_device()
    {
        var snapshot = SnapshotWithActiveOldGeneration();
        var remoteReset = CreateRemoteReset(OldGeneration);

        var plan = Plan(snapshot, resetEvents: [remoteReset]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretReset);
        Assert.Single(plan.ApplyBatch.SecretResetEvents);
    }

    [Fact]
    public void Case3_one_sided_reset_new_requires_remote_reset_confirmation_before_new_profile_is_accepted()
    {
        var snapshot = SnapshotWithActiveOldGeneration();
        var remoteReset = CreateRemoteReset(OldGeneration);
        var remoteNewProfile = CreateRemoteProfile(NewGenerationA, contentHash: "sha256:pa2-profile");

        var plan = Plan(snapshot, resetEvents: [remoteReset], cryptoProfiles: [remoteNewProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretReset);
    }

    [Fact]
    public void Case4_two_independent_password_changes_second_device_requires_password_change_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    OldGeneration,
                    BookmarkSyncState.Dirty,
                    "sha256:pb-profile")
            ]
        };
        var remoteProfile = CreateRemoteProfile(
            OldGeneration,
            contentHash: "sha256:pa-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(snapshot, cryptoProfiles: [remoteProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.CryptoProfile, OldGeneration), conflict.Identity);
    }

    [Fact]
    public void Case5_two_reset_only_same_generation_is_idempotent_for_second_device()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration)
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);

        var plan = Plan(snapshot, resetEvents: [remoteReset]);

        Assert.Null(plan.SecretConflictConfirmation);
        Assert.Empty(plan.ApplyBatch.SecretResetEvents);
        var satisfied = Assert.Single(plan.SatisfiedResetEvents);
        Assert.Equal(OldGeneration, satisfied.SecretGenerationId);
    }

    [Fact]
    public void Case6a_password_change_wins_over_local_reset_only_requires_password_change_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration, baselineContentHash: "sha256:p0-profile")
            ]
        };
        var remoteChangedProfile = CreateRemoteProfile(
            OldGeneration,
            contentHash: "sha256:pa-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(snapshot, cryptoProfiles: [remoteChangedProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange);
    }

    [Fact]
    public void Case6b_reset_only_wins_over_local_password_change_requires_remote_reset_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    OldGeneration,
                    BookmarkSyncState.Dirty,
                    "sha256:pa-profile")
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);

        var plan = Plan(snapshot, resetEvents: [remoteReset]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretReset);
    }

    [Fact]
    public void Case7a_password_change_wins_over_local_reset_new_requires_password_change_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    NewGenerationB,
                    BookmarkSyncState.Dirty,
                    "sha256:pb2-profile",
                    updatedAtUtc: Now.AddMinutes(2))
            ],
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration, baselineContentHash: "sha256:p0-profile")
            ]
        };
        var remoteChangedProfile = CreateRemoteProfile(
            OldGeneration,
            contentHash: "sha256:pa-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(snapshot, cryptoProfiles: [remoteChangedProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange);
    }

    [Fact]
    public void Case7b_reset_new_wins_over_local_password_change_requires_remote_reset_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    OldGeneration,
                    BookmarkSyncState.Dirty,
                    "sha256:pa-profile")
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);
        var remoteNewProfile = CreateRemoteProfile(NewGenerationB, contentHash: "sha256:pb2-profile");

        var plan = Plan(
            snapshot,
            resetEvents: [remoteReset],
            cryptoProfiles: [remoteNewProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretReset);
    }

    [Fact]
    public void Case8a_reset_only_then_local_reset_new_can_upload_local_new_generation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    NewGenerationB,
                    BookmarkSyncState.Dirty,
                    "sha256:pb2-profile")
            ],
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration, baselineContentHash: "sha256:p0-profile")
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);
        var remoteOldProfile = CreateRemoteProfile(OldGeneration, contentHash: "sha256:p0-profile");

        var pullPlan = Plan(
            snapshot,
            resetEvents: [remoteReset],
            cryptoProfiles: [remoteOldProfile]);
        var pushPlan = SyncPushPlanner.Plan(snapshot);

        Assert.Null(pullPlan.SecretConflictConfirmation);
        Assert.Single(pullPlan.SatisfiedResetEvents);
        Assert.Equal(NewGenerationB, Assert.Single(pushPlan.CryptoProfiles).Profile.SecretGenerationId);
    }

    [Fact]
    public void Case8b_reset_new_remote_is_accepted_when_local_has_reset_only()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration, baselineContentHash: "sha256:p0-profile")
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);
        var remoteOldProfile = CreateRemoteProfile(OldGeneration, contentHash: "sha256:p0-profile");
        var remoteNewProfile = CreateRemoteProfile(
            NewGenerationB,
            contentHash: "sha256:pb2-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(
            snapshot,
            resetEvents: [remoteReset],
            cryptoProfiles: [remoteOldProfile, remoteNewProfile]);

        Assert.Null(plan.SecretConflictConfirmation);
        Assert.Single(plan.SatisfiedResetEvents);
        Assert.Equal(NewGenerationB, Assert.Single(plan.ApplyBatch.CryptoProfiles).Value.SecretGenerationId);
    }

    [Fact]
    public void Case9_two_independent_reset_new_second_device_requires_password_change_confirmation()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(
                    NewGenerationB,
                    BookmarkSyncState.Dirty,
                    "sha256:pb2-profile",
                    updatedAtUtc: Now.AddMinutes(2))
            ],
            SecretResetEvents =
            [
                CreateLocalReset(OldGeneration, baselineContentHash: "sha256:p0-profile")
            ]
        };
        var remoteReset = CreateRemoteReset(OldGeneration);
        var remoteOldProfile = CreateRemoteProfile(OldGeneration, contentHash: "sha256:p0-profile");
        var remoteNewProfile = CreateRemoteProfile(
            NewGenerationA,
            contentHash: "sha256:pa2-profile",
            updatedAtUtc: Now.AddMinutes(1));

        var plan = Plan(
            snapshot,
            resetEvents: [remoteReset],
            cryptoProfiles: [remoteOldProfile, remoteNewProfile]);

        AssertSecretConflict(plan, SyncSecretConflictConfirmationReason.RemoteSecretPasswordChange);
        Assert.Empty(plan.ApplyBatch.CryptoProfiles);
    }

    private static SyncPullPlan Plan(
        SyncLocalSnapshot snapshot,
        SyncSecretResetEventDto[]? resetEvents = null,
        SyncCryptoProfileDto[]? cryptoProfiles = null)
    {
        return SyncPullPlanner.Plan(
            snapshot,
            (resetEvents ?? []).Select(resetEvent =>
                Success(SyncObjectKind.SecretResetEvent, resetEvent.SecretGenerationId, resetEvent)).ToArray(),
            (cryptoProfiles ?? []).Select(profile =>
                Success(SyncObjectKind.CryptoProfile, profile.SecretGenerationId, profile)).ToArray(),
            [],
            [],
            []);
    }

    private static void AssertSecretConflict(
        SyncPullPlan plan,
        SyncSecretConflictConfirmationReason expectedReason)
    {
        Assert.NotNull(plan.SecretConflictConfirmation);
        Assert.Equal(expectedReason, plan.SecretConflictConfirmation!.Reason);
    }

    private static SyncLocalSnapshot SnapshotWithActiveOldGeneration()
    {
        return EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateLocalProfile(OldGeneration, contentHash: "sha256:p0-profile")
            ]
        };
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

    private static SyncCryptoProfileSnapshotRecord CreateLocalProfile(
        string secretGenerationId,
        BookmarkSyncState syncState = BookmarkSyncState.Clean,
        string contentHash = "sha256:profile",
        DateTimeOffset? updatedAtUtc = null)
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
            UpdatedAtUtc: updatedAtUtc ?? Now,
            SecretGenerationId: secretGenerationId);

        return new SyncCryptoProfileSnapshotRecord(
            profile,
            new SyncObjectMetadata(
                syncState,
                RemoteEtag: "profile-etag",
                LastSyncedAtUtc: Now,
                ContentHash: contentHash,
                ModifiedDeviceId: "device"));
    }

    private static SecretResetEventRecord CreateLocalReset(
        string secretGenerationId,
        string baselineContentHash = "sha256:p0-profile")
    {
        return new SecretResetEventRecord(
            $"reset-{secretGenerationId}",
            secretGenerationId,
            Now,
            "device",
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            BaselineCryptoProfileContentHash: baselineContentHash,
            BaselineCryptoProfileRemoteEtag: "profile-etag");
    }

    private static SyncCryptoProfileDto CreateRemoteProfile(
        string secretGenerationId,
        string contentHash,
        DateTimeOffset? updatedAtUtc = null)
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
            UpdatedAtUtc: updatedAtUtc ?? Now,
            ModifiedDeviceId: "device",
            ContentHash: contentHash);
    }

    private static SyncSecretResetEventDto CreateRemoteReset(string secretGenerationId)
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

    private const string OldGeneration = "old-generation";
    private const string NewGenerationA = "new-generation-a";
    private const string NewGenerationB = "new-generation-b";
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
}
