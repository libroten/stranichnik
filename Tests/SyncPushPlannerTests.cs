using System;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Push;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncPushPlannerTests
{
    [Fact]
    public void Plan_selects_dirty_objects_in_safe_categories()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateResetEvent("reset-generation", BookmarkSyncState.Dirty)
            ],
            CryptoProfiles =
            [
                CreateProfile("profile-generation", BookmarkSyncState.Dirty)
            ],
            IconAssets =
            [
                CreateIconAsset("icon", BookmarkSyncState.Dirty)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset("secret-icon", "profile-generation", BookmarkSyncState.Dirty)
            ],
            Items =
            [
                CreateItem("clean-item", BookmarkSyncState.Clean),
                CreateItem("dirty-item", BookmarkSyncState.Dirty)
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Equal("reset-generation", Assert.Single(plan.SecretResetEvents).SecretGenerationId);
        Assert.Equal("profile-generation", Assert.Single(plan.CryptoProfiles).Profile.SecretGenerationId);
        Assert.Equal("icon", Assert.Single(plan.IconAssets).Asset.Id);
        Assert.Equal("secret-icon", Assert.Single(plan.SecretIconAssets).Asset.Id);
        Assert.Equal("dirty-item", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_skips_secret_objects_invalidated_by_reset_event()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateResetEvent("reset-generation", BookmarkSyncState.Dirty)
            ],
            CryptoProfiles =
            [
                CreateProfile("reset-generation", BookmarkSyncState.Dirty)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset("reset-secret-icon", "reset-generation", BookmarkSyncState.Dirty)
            ],
            Items =
            [
                CreateSecretItem(
                    "reset-secret-item",
                    cryptoProfileId: 1,
                    BookmarkSyncState.Dirty,
                    secretGenerationId: "reset-generation")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Single(plan.SecretResetEvents);
        Assert.Empty(plan.CryptoProfiles);
        Assert.Empty(plan.SecretIconAssets);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Plan_skips_secret_item_when_matching_profile_is_missing()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateSecretItem("secret-item", cryptoProfileId: 42, BookmarkSyncState.Dirty)
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Plan_skips_secret_objects_when_matching_profile_is_conflicted()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateProfile("profile-generation", BookmarkSyncState.Conflict, hasBeenSynced: false)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset("secret-icon", "profile-generation", BookmarkSyncState.Dirty)
            ],
            Items =
            [
                CreateSecretItem(
                    "secret-item",
                    cryptoProfileId: 1,
                    BookmarkSyncState.Dirty,
                    secretGenerationId: "profile-generation")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.CryptoProfiles);
        Assert.Empty(plan.SecretIconAssets);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Plan_skips_clean_synced_parent_folders_for_dirty_child()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateFolder("root-folder", parentId: null, syncState: BookmarkSyncState.Clean),
                CreateFolder("nested-folder", parentId: "root-folder", syncState: BookmarkSyncState.Clean),
                CreateItem("dirty-bookmark", BookmarkSyncState.Dirty, parentId: "nested-folder")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Equal("dirty-bookmark", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_includes_never_synced_parent_folders_for_dirty_child()
    {
        var snapshot = EmptySnapshot() with
        {
            Items =
            [
                CreateFolder("root-folder", parentId: null, syncState: BookmarkSyncState.Clean, hasBeenSynced: false),
                CreateFolder("nested-folder", parentId: "root-folder", syncState: BookmarkSyncState.Clean, hasBeenSynced: false),
                CreateItem("dirty-bookmark", BookmarkSyncState.Dirty, parentId: "nested-folder")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Collection(
            plan.Items.OrderBy(item => item.Item.Id, StringComparer.Ordinal),
            item => Assert.Equal("dirty-bookmark", item.Item.Id),
            item => Assert.Equal("nested-folder", item.Item.Id),
            item => Assert.Equal("root-folder", item.Item.Id));
    }

    [Fact]
    public void Plan_skips_clean_synced_icon_asset_referenced_by_dirty_item()
    {
        var snapshot = EmptySnapshot() with
        {
            IconAssets =
            [
                CreateIconAsset("clean-icon", BookmarkSyncState.Clean)
            ],
            Items =
            [
                CreateItem("dirty-bookmark", BookmarkSyncState.Dirty, iconAssetId: "clean-icon")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.IconAssets);
        Assert.Equal("dirty-bookmark", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_skips_clean_synced_crypto_profile_for_dirty_secret_item()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateProfile("profile-generation", BookmarkSyncState.Clean)
            ],
            Items =
            [
                CreateSecretItem("dirty-secret-bookmark", cryptoProfileId: 1, BookmarkSyncState.Dirty)
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.CryptoProfiles);
        Assert.Equal("dirty-secret-bookmark", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_skips_clean_synced_secret_icon_asset_for_dirty_secret_item()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateProfile("profile-generation", BookmarkSyncState.Clean)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset("clean-secret-icon", "profile-generation", BookmarkSyncState.Clean)
            ],
            Items =
            [
                CreateSecretItem(
                    "dirty-secret-bookmark",
                    cryptoProfileId: 1,
                    BookmarkSyncState.Dirty,
                    secretIconAssetId: "clean-secret-icon")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.CryptoProfiles);
        Assert.Empty(plan.SecretIconAssets);
        Assert.Equal("dirty-secret-bookmark", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_includes_never_synced_dependencies_referenced_by_dirty_items()
    {
        var snapshot = EmptySnapshot() with
        {
            CryptoProfiles =
            [
                CreateProfile("profile-generation", BookmarkSyncState.Clean, hasBeenSynced: false)
            ],
            IconAssets =
            [
                CreateIconAsset("never-synced-icon", BookmarkSyncState.Clean, hasBeenSynced: false)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset(
                    "never-synced-secret-icon",
                    "profile-generation",
                    BookmarkSyncState.Clean,
                    hasBeenSynced: false)
            ],
            Items =
            [
                CreateItem("dirty-bookmark", BookmarkSyncState.Dirty, iconAssetId: "never-synced-icon"),
                CreateSecretItem(
                    "dirty-secret-bookmark",
                    cryptoProfileId: 1,
                    BookmarkSyncState.Dirty,
                    secretIconAssetId: "never-synced-secret-icon")
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Equal("profile-generation", Assert.Single(plan.CryptoProfiles).Profile.SecretGenerationId);
        Assert.Equal("never-synced-icon", Assert.Single(plan.IconAssets).Asset.Id);
        Assert.Equal("never-synced-secret-icon", Assert.Single(plan.SecretIconAssets).Asset.Id);
        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public void Plan_includes_clean_objects_that_were_never_synced()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateResetEvent("never-synced-reset", BookmarkSyncState.Clean, hasBeenSynced: false)
            ],
            CryptoProfiles =
            [
                CreateProfile("never-synced-profile", BookmarkSyncState.Clean, hasBeenSynced: false)
            ],
            IconAssets =
            [
                CreateIconAsset("never-synced-icon", BookmarkSyncState.Clean, hasBeenSynced: false)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset(
                    "never-synced-secret-icon",
                    "never-synced-profile",
                    BookmarkSyncState.Clean,
                    hasBeenSynced: false)
            ],
            Items =
            [
                CreateItem("never-synced-bookmark", BookmarkSyncState.Clean, hasBeenSynced: false)
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Equal("never-synced-reset", Assert.Single(plan.SecretResetEvents).SecretGenerationId);
        Assert.Equal("never-synced-profile", Assert.Single(plan.CryptoProfiles).Profile.SecretGenerationId);
        Assert.Equal("never-synced-icon", Assert.Single(plan.IconAssets).Asset.Id);
        Assert.Equal("never-synced-secret-icon", Assert.Single(plan.SecretIconAssets).Asset.Id);
        Assert.Equal("never-synced-bookmark", Assert.Single(plan.Items).Item.Id);
    }

    [Fact]
    public void Plan_skips_clean_objects_that_were_already_synced()
    {
        var snapshot = EmptySnapshot() with
        {
            SecretResetEvents =
            [
                CreateResetEvent("already-synced-reset", BookmarkSyncState.Clean)
            ],
            CryptoProfiles =
            [
                CreateProfile("already-synced-profile", BookmarkSyncState.Clean)
            ],
            IconAssets =
            [
                CreateIconAsset("already-synced-icon", BookmarkSyncState.Clean)
            ],
            SecretIconAssets =
            [
                CreateSecretIconAsset("already-synced-secret-icon", "already-synced-profile", BookmarkSyncState.Clean)
            ],
            Items =
            [
                CreateItem("already-synced-bookmark", BookmarkSyncState.Clean)
            ]
        };

        var plan = SyncPushPlanner.Plan(snapshot);

        Assert.Empty(plan.SecretResetEvents);
        Assert.Empty(plan.CryptoProfiles);
        Assert.Empty(plan.IconAssets);
        Assert.Empty(plan.SecretIconAssets);
        Assert.Empty(plan.Items);
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

    private static SecretResetEventRecord CreateResetEvent(
        string secretGenerationId,
        BookmarkSyncState syncState,
        bool hasBeenSynced = true)
    {
        return new SecretResetEventRecord(
            secretGenerationId,
            secretGenerationId,
            Now,
            "device",
            syncState,
            RemoteEtag: null,
            LastSyncedAtUtc: hasBeenSynced ? Now : null);
    }

    private static SyncCryptoProfileSnapshotRecord CreateProfile(
        string secretGenerationId,
        BookmarkSyncState syncState,
        bool hasBeenSynced = true)
    {
        return new SyncCryptoProfileSnapshotRecord(
            new CryptoProfileRecord(
                Id: 1,
                ProfileVersion: 1,
                KdfName: "PBKDF2",
                KdfHashAlgorithm: "SHA256",
                KdfIterations: 1000,
                KdfSalt: new byte[] { 1 },
                KekLengthBytes: 32,
                DataKeyAlgorithm: "AES",
                WrappedDataKey: new byte[] { 2 },
                WrappedDataKeyNonce: new byte[] { 3 },
                EncryptionAlgorithm: "AES-GCM",
                PayloadFormat: "v1",
                PasswordCheckPayload: new byte[] { 4 },
                PasswordCheckNonce: new byte[] { 5 },
                CreatedAtUtc: Now,
                UpdatedAtUtc: Now,
                SecretGenerationId: secretGenerationId),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncIconAssetSnapshotRecord CreateIconAsset(
        string id,
        BookmarkSyncState syncState,
        bool hasBeenSynced = true)
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
                ProcessedBytes: new byte[] { 1, 2, 3 },
                CreatedAtUtc: Now),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncSecretIconAssetSnapshotRecord CreateSecretIconAsset(
        string id,
        string secretGenerationId,
        BookmarkSyncState syncState,
        bool hasBeenSynced = true)
    {
        return new SyncSecretIconAssetSnapshotRecord(
            new SecretIconAssetRecord(
                id,
                "sha256",
                "secret-source-hash",
                SourceSizeBytes: 3,
                ProcessedMimeType: "image/png",
                ProcessedWidth: 32,
                ProcessedHeight: 32,
                EncryptedProcessedBytes: new EncryptedSecretIconPayloadRecord(
                    Payload: new byte[] { 1, 2, 3 },
                    Nonce: new byte[] { 4, 5, 6 },
                    PayloadFormatVersion: 1),
                SecretGenerationId: secretGenerationId,
                CreatedAtUtc: Now),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncItemSnapshotRecord CreateItem(
        string id,
        BookmarkSyncState syncState,
        string? parentId = null,
        string? iconAssetId = null,
        bool hasBeenSynced = true)
    {
        return new SyncItemSnapshotRecord(
            new BookmarkItemRecord(
                id,
                parentId,
                Kind: BookmarkItemKind.Bookmark,
                SortOrder: 1000,
                Title: "Title",
                Url: "https://example.com/",
                IsSecret: false,
                EncryptedPayload: null,
                SecretGenerationId: null,
                Metadata: new BookmarkItemMetadata(
                    Now,
                    Now,
                    DeletedAtUtc: null,
                    Revision: 1,
                    syncState,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    ContentHash: "sha256:item",
                    ModifiedDeviceId: "device"),
                iconAssetId),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncItemSnapshotRecord CreateFolder(
        string id,
        string? parentId,
        BookmarkSyncState syncState,
        bool hasBeenSynced = true)
    {
        return new SyncItemSnapshotRecord(
            new BookmarkItemRecord(
                id,
                parentId,
                Kind: BookmarkItemKind.Folder,
                SortOrder: 1000,
                Title: "Folder",
                Url: null,
                IsSecret: false,
                EncryptedPayload: null,
                SecretGenerationId: null,
                Metadata: new BookmarkItemMetadata(
                    Now,
                    Now,
                    DeletedAtUtc: null,
                    Revision: 1,
                    syncState,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    ContentHash: "sha256:folder",
                    ModifiedDeviceId: "device")),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncItemSnapshotRecord CreateSecretItem(
        string id,
        long cryptoProfileId,
        BookmarkSyncState syncState,
        string? secretIconAssetId = null,
        bool hasBeenSynced = true,
        string secretGenerationId = "profile-generation")
    {
        return new SyncItemSnapshotRecord(
            new BookmarkItemRecord(
                id,
                ParentId: null,
                Kind: BookmarkItemKind.Bookmark,
                SortOrder: 1000,
                Title: null,
                Url: null,
                IsSecret: true,
                EncryptedPayload: new EncryptedBookmarkPayloadRecord(
                    Payload: new byte[] { 1, 2, 3 },
                    Nonce: new byte[] { 4, 5, 6 },
                    CryptoProfileId: cryptoProfileId),
                SecretGenerationId: secretGenerationId,
                Metadata: new BookmarkItemMetadata(
                    Now,
                    Now,
                    DeletedAtUtc: null,
                    Revision: 1,
                    syncState,
                    RemoteEtag: null,
                    LastSyncedAtUtc: null,
                    ContentHash: "sha256:secret-item",
                    ModifiedDeviceId: "device"),
                SecretIconAssetId: secretIconAssetId),
            CreateMetadata(syncState, hasBeenSynced));
    }

    private static SyncObjectMetadata CreateMetadata(
        BookmarkSyncState syncState,
        bool hasBeenSynced)
    {
        return new SyncObjectMetadata(
            syncState,
            RemoteEtag: null,
            LastSyncedAtUtc: hasBeenSynced ? Now : null,
            ContentHash: "sha256:content",
            ModifiedDeviceId: "device");
    }

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
