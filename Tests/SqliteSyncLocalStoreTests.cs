using System;
using System.IO;
using System.Linq;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
using Stranichnik.Sync;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteSyncLocalStoreTests
{
    [Fact]
    public void LoadSnapshot_includes_reset_events_and_sync_metadata()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("reset-generation"));
        database.ResetStore.ResetMasterPasswordAndPurgeSecrets("reset-generation");
        database.ProfileStore.SaveNewProfile(CreateProfile("active-generation"));
        database.InsertBookmark("bookmark");
        database.InsertDeletedBookmark("deleted-bookmark");
        database.InsertIconAsset("icon");
        database.InsertSecretIconAsset("secret-icon", "active-generation");
        database.SyncMetadataStore.UpsertPendingAssetRef(new SyncPendingAssetRefRecord(
            Id: "pending",
            ItemId: "bookmark",
            AssetKind: SyncPendingAssetKind.RegularIcon,
            RemoteAssetId: "remote-icon",
            SourceHashAlgorithm: null,
            SourceHash: null,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        database.SyncMetadataStore.UpsertDeferredSecretItem(new SyncDeferredSecretItemRecord(
            RemoteItemId: "remote-secret",
            SecretGenerationId: "generation",
            RemoteEtag: null,
            ContentHash: "sha256:content",
            CanonicalJson: DeferredJson,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        database.SyncMetadataStore.UpsertQuarantinedRemoteObject(new SyncQuarantinedRemoteObjectRecord(
            Id: "quarantine",
            ObjectKind: "item",
            RelativePath: "items/item.json",
            RemoteEtag: null,
            ContentHash: null,
            ReasonCode: "invalid-json",
            FirstSeenAtUtc: Now,
            LastSeenAtUtc: Now,
            SeenCount: 1));

        var snapshot = database.SyncLocalStore.LoadSnapshot();

        Assert.Equal("database-id", snapshot.Identity.DatabaseId);
        Assert.Equal("device-id", snapshot.Identity.DeviceId);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.Contains(snapshot.Items, item => item.Item.Id == "bookmark" && item.Item.Metadata.DeletedAtUtc is null);
        Assert.Contains(snapshot.Items, item => item.Item.Id == "deleted-bookmark" && item.Item.Metadata.DeletedAtUtc is not null);
        Assert.Contains(snapshot.Items, item => item.Item.Id == "bookmark" && item.SyncMetadata.ContentHash == "sha256:item-content");
        Assert.DoesNotContain(database.TreeStore.Load().Items, item => item.Id == "deleted-bookmark");
        var iconAsset = Assert.Single(snapshot.IconAssets);
        Assert.Equal("icon", iconAsset.Asset.Id);
        Assert.Equal(BookmarkSyncState.Dirty, iconAsset.SyncMetadata.SyncState);
        Assert.Equal("sha256:icon-content", iconAsset.SyncMetadata.ContentHash);
        Assert.Equal("test-device", iconAsset.SyncMetadata.ModifiedDeviceId);
        var secretIconAsset = Assert.Single(snapshot.SecretIconAssets);
        Assert.Equal("secret-icon", secretIconAsset.Asset.Id);
        Assert.Equal("active-generation", secretIconAsset.Asset.SecretGenerationId);
        Assert.Equal(BookmarkSyncState.Dirty, secretIconAsset.SyncMetadata.SyncState);
        Assert.Equal("sha256:secret-icon-content", secretIconAsset.SyncMetadata.ContentHash);
        var cryptoProfile = Assert.Single(snapshot.CryptoProfiles);
        Assert.Equal("active-generation", cryptoProfile.Profile.SecretGenerationId);
        Assert.Equal(BookmarkSyncState.Dirty, cryptoProfile.SyncMetadata.SyncState);
        Assert.Single(snapshot.SecretResetEvents);
        Assert.Single(snapshot.PendingAssetRefs);
        Assert.Single(snapshot.DeferredSecretItems);
        Assert.Single(snapshot.QuarantinedRemoteObjects);
    }

    [Fact]
    public void ApplyRemoteChanges_upserts_icon_assets_and_crypto_profiles_as_clean()
    {
        using var database = TempSqliteDatabase.Create();
        var iconAsset = CreateRemoteIconAsset("remote-icon");
        var secretIconAsset = CreateRemoteSecretIconAsset("remote-secret-icon", "remote-generation");
        var cryptoProfile = CreateRemoteCryptoProfile("remote-generation");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [Applied(SyncObjectKind.CryptoProfile, cryptoProfile.SecretGenerationId, cryptoProfile)],
            IconAssets: [Applied(SyncObjectKind.IconAsset, iconAsset.Id, iconAsset)],
            SecretIconAssets: [Applied(SyncObjectKind.SecretIconAsset, secretIconAsset.Id, secretIconAsset)],
            Items: []);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var snapshot = database.SyncLocalStore.LoadSnapshot();
        var storedIconAsset = Assert.Single(snapshot.IconAssets);
        Assert.Equal("remote-icon", storedIconAsset.Asset.Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, storedIconAsset.Asset.ProcessedBytes.ToArray());
        Assert.Equal(BookmarkSyncState.Clean, storedIconAsset.SyncMetadata.SyncState);
        Assert.Equal("remote-etag", storedIconAsset.SyncMetadata.RemoteEtag);
        Assert.Equal("sha256:remote-icon-content", storedIconAsset.SyncMetadata.ContentHash);
        var storedProfile = Assert.Single(snapshot.CryptoProfiles);
        Assert.Equal("remote-generation", storedProfile.Profile.SecretGenerationId);
        Assert.Equal(PasswordCheckPayload, storedProfile.Profile.PasswordCheckPayload.ToArray());
        Assert.Equal(PasswordCheckNonce, storedProfile.Profile.PasswordCheckNonce.ToArray());
        Assert.Equal(BookmarkSyncState.Clean, storedProfile.SyncMetadata.SyncState);
        Assert.Equal("sha256:remote-profile-content", storedProfile.SyncMetadata.ContentHash);
        var storedSecretIconAsset = Assert.Single(snapshot.SecretIconAssets);
        Assert.Equal("remote-secret-icon", storedSecretIconAsset.Asset.Id);
        Assert.Equal("remote-generation", storedSecretIconAsset.Asset.SecretGenerationId);
        Assert.Equal(new byte[] { 4, 5, 6 }, storedSecretIconAsset.Asset.EncryptedProcessedBytes.Payload.ToArray());
        Assert.Equal(new byte[] { 7, 8, 9 }, storedSecretIconAsset.Asset.EncryptedProcessedBytes.Nonce.ToArray());
        Assert.Equal(BookmarkSyncState.Clean, storedSecretIconAsset.SyncMetadata.SyncState);
        Assert.Equal("sha256:remote-secret-icon-content", storedSecretIconAsset.SyncMetadata.ContentHash);
    }

    [Fact]
    public void ApplyRemoteChanges_applies_secret_reset_before_other_remote_objects()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.InsertBookmark("normal");
        database.InsertSecretBookmark("secret", "generation");
        database.InsertSecretOnlyFolder("secret-folder");
        database.InsertSecretBookmark("nested-secret", "generation", parentId: "secret-folder");
        database.InsertSecretIconAsset("secret-icon", "generation");
        database.SyncMetadataStore.UpsertDeferredSecretItem(new SyncDeferredSecretItemRecord(
            RemoteItemId: "remote-secret",
            SecretGenerationId: "generation",
            RemoteEtag: null,
            ContentHash: "sha256:remote-secret-content",
            CanonicalJson: DeferredJson,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        var resetEvent = CreateRemoteResetEvent("generation");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [Applied(SyncObjectKind.SecretResetEvent, resetEvent.SecretGenerationId, resetEvent)],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: []);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Null(database.ProfileStore.LoadActiveProfile());
        Assert.Null(database.TreeStore.GetSecretIconAsset("secret-icon"));
        var visibleItems = database.TreeStore.Load().Items;
        Assert.Single(visibleItems);
        Assert.Equal("normal", visibleItems[0].Id);
        var storedResetEvent = Assert.Single(database.ResetStore.LoadResetEvents());
        Assert.Equal(BookmarkSyncState.Clean, storedResetEvent.SyncState);
        Assert.Equal("remote-etag", storedResetEvent.RemoteEtag);
        Assert.Empty(database.SyncMetadataStore.LoadDeferredSecretItems());
    }

    [Fact]
    public void ApplyRemoteChanges_upserts_plaintext_items_as_clean()
    {
        using var database = TempSqliteDatabase.Create();
        var folder = CreateRemoteFolder("remote-folder");
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: "remote-folder");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items:
            [
                Applied(SyncObjectKind.Item, folder.Id, folder),
                Applied(SyncObjectKind.Item, bookmark.Id, bookmark)
            ]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var visibleItems = database.TreeStore.Load().Items;
        Assert.Equal(2, visibleItems.Count);
        Assert.Contains(visibleItems, item => item.Id == "remote-folder" && item.Kind == BookmarkItemKind.Folder);
        Assert.Contains(visibleItems, item => item.Id == "remote-bookmark" && item.ParentId == "remote-folder");
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-bookmark");
        Assert.Equal(BookmarkSyncState.Clean, storedItem.SyncMetadata.SyncState);
        Assert.Equal("sha256:remote-bookmark-content", storedItem.SyncMetadata.ContentHash);
    }

    [Fact]
    public void ApplyRemoteChanges_applies_child_before_parent_when_parent_is_in_same_batch()
    {
        using var database = TempSqliteDatabase.Create();
        var folder = CreateRemoteFolder("remote-folder");
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: "remote-folder");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items:
            [
                Applied(SyncObjectKind.Item, bookmark.Id, bookmark),
                Applied(SyncObjectKind.Item, folder.Id, folder)
            ]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var storedBookmark = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-bookmark");
        Assert.Equal("remote-folder", storedBookmark.Item.ParentId);
        Assert.Empty(database.SyncMetadataStore.LoadQuarantinedRemoteObjects());
    }

    [Fact]
    public void ApplyRemoteChanges_quarantines_item_with_missing_parent()
    {
        using var database = TempSqliteDatabase.Create();
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: "missing-parent");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, bookmark.Id, bookmark)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.SyncLocalStore.LoadSnapshot().Items);
        var quarantine = Assert.Single(database.SyncMetadataStore.LoadQuarantinedRemoteObjects());
        Assert.Equal("Item:remote-bookmark", quarantine.Id);
        Assert.Equal(SyncObjectKind.Item.ToString(), quarantine.ObjectKind);
        Assert.Equal("items/remote-bookmark.json", quarantine.RelativePath);
        Assert.Equal("missing-parent", quarantine.ReasonCode);
        Assert.Equal("sha256:remote-bookmark-content", quarantine.ContentHash);
    }

    [Fact]
    public void ApplyRemoteChanges_applies_plaintext_item_tombstone()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertBookmark("bookmark");
        var tombstone = CreateRemoteBookmark("bookmark", parentId: null) with
        {
            DeletedAtUtc = Now.AddDays(1),
            ContentHash = "sha256:remote-tombstone-content"
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, tombstone.Id, tombstone)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.TreeStore.Load().Items);
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "bookmark");
        Assert.NotNull(storedItem.Item.Metadata.DeletedAtUtc);
        Assert.Equal("sha256:remote-tombstone-content", storedItem.SyncMetadata.ContentHash);
    }

    [Fact]
    public void ApplyRemoteChanges_applies_child_tombstone_after_parent_tombstone()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertFolder("folder");
        database.InsertBookmark("bookmark", parentId: "folder");
        var folderTombstone = CreateRemoteFolder("folder") with
        {
            DeletedAtUtc = Now.AddDays(1),
            ContentHash = "sha256:remote-folder-tombstone-content"
        };
        var bookmarkTombstone = CreateRemoteBookmark("bookmark", parentId: "folder") with
        {
            DeletedAtUtc = Now.AddDays(1),
            ContentHash = "sha256:remote-bookmark-tombstone-content"
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items:
            [
                Applied(SyncObjectKind.Item, folderTombstone.Id, folderTombstone),
                Applied(SyncObjectKind.Item, bookmarkTombstone.Id, bookmarkTombstone)
            ]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.TreeStore.Load().Items);
        var snapshot = database.SyncLocalStore.LoadSnapshot();
        var storedFolder = Assert.Single(snapshot.Items, item => item.Item.Id == "folder");
        var storedBookmark = Assert.Single(snapshot.Items, item => item.Item.Id == "bookmark");
        Assert.NotNull(storedFolder.Item.Metadata.DeletedAtUtc);
        Assert.NotNull(storedBookmark.Item.Metadata.DeletedAtUtc);
        Assert.Empty(database.SyncMetadataStore.LoadQuarantinedRemoteObjects());
    }

    [Fact]
    public void ApplyRemoteChanges_quarantines_tombstone_with_unknown_parent()
    {
        using var database = TempSqliteDatabase.Create();
        var tombstone = CreateRemoteBookmark("bookmark", parentId: "missing-parent") with
        {
            DeletedAtUtc = Now.AddDays(1),
            ContentHash = "sha256:remote-tombstone-content"
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, tombstone.Id, tombstone)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.TreeStore.Load().Items);
        var quarantine = Assert.Single(database.SyncMetadataStore.LoadQuarantinedRemoteObjects());
        Assert.Equal("Item:bookmark", quarantine.Id);
        Assert.Equal("missing-parent", quarantine.ReasonCode);
    }

    [Fact]
    public void ApplyRemoteChanges_defers_missing_regular_icon_reference()
    {
        using var database = TempSqliteDatabase.Create();
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: null) with
        {
            IconAssetRef = new SyncAssetReferenceDto(
                AssetId: "missing-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "missing-source-hash")
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, bookmark.Id, bookmark)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-bookmark");
        Assert.Null(storedItem.Item.IconAssetId);
        var pendingRef = Assert.Single(database.SyncMetadataStore.LoadPendingAssetRefs());
        Assert.Equal("remote-bookmark", pendingRef.ItemId);
        Assert.Equal(SyncPendingAssetKind.RegularIcon, pendingRef.AssetKind);
        Assert.Equal("missing-icon", pendingRef.RemoteAssetId);
        Assert.Equal("sha256", pendingRef.SourceHashAlgorithm);
        Assert.Equal("missing-source-hash", pendingRef.SourceHash);
    }

    [Fact]
    public void ApplyRemoteChanges_resolves_pending_regular_icon_reference_after_icon_arrives()
    {
        using var database = TempSqliteDatabase.Create();
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: null) with
        {
            IconAssetRef = new SyncAssetReferenceDto(
                AssetId: "remote-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "remote-source-hash")
        };
        var firstBatch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, bookmark.Id, bookmark)]);
        var iconAsset = CreateRemoteIconAsset("remote-icon");
        var secondBatch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [Applied(SyncObjectKind.IconAsset, iconAsset.Id, iconAsset)],
            SecretIconAssets: [],
            Items: []);

        database.SyncLocalStore.ApplyRemoteChanges(firstBatch);
        database.SyncLocalStore.ApplyRemoteChanges(secondBatch);

        Assert.Empty(database.SyncMetadataStore.LoadPendingAssetRefs());
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-bookmark");
        Assert.Equal("remote-icon", storedItem.Item.IconAssetId);
        Assert.Equal(BookmarkSyncState.Clean, storedItem.SyncMetadata.SyncState);
    }

    [Fact]
    public void ApplyRemoteChanges_uses_existing_regular_icon_reference_and_clears_pending_ref()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertIconAsset("existing-icon");
        database.InsertBookmark("remote-bookmark");
        database.SyncMetadataStore.UpsertPendingAssetRef(new SyncPendingAssetRefRecord(
            Id: "old-pending",
            ItemId: "remote-bookmark",
            AssetKind: SyncPendingAssetKind.RegularIcon,
            RemoteAssetId: "old-missing-icon",
            SourceHashAlgorithm: null,
            SourceHash: null,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        var bookmark = CreateRemoteBookmark("remote-bookmark", parentId: null) with
        {
            IconAssetRef = new SyncAssetReferenceDto(
                AssetId: "existing-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "source-hash")
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, bookmark.Id, bookmark)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-bookmark");
        Assert.Equal("existing-icon", storedItem.Item.IconAssetId);
        Assert.Empty(database.SyncMetadataStore.LoadPendingAssetRefs());
    }

    [Fact]
    public void ApplyRemoteChanges_defers_secret_item_without_inserting_visible_item()
    {
        using var database = TempSqliteDatabase.Create();
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "missing-generation");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, secretItem.Id, secretItem)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.TreeStore.Load().Items);
        Assert.Empty(database.SyncLocalStore.LoadSnapshot().Items);
        var deferredItem = Assert.Single(database.SyncMetadataStore.LoadDeferredSecretItems());
        Assert.Equal("remote-secret", deferredItem.RemoteItemId);
        Assert.Equal("missing-generation", deferredItem.SecretGenerationId);
        Assert.Equal("remote-etag", deferredItem.RemoteEtag);
        Assert.Equal("sha256:remote-secret-content", deferredItem.ContentHash);
        Assert.Equal(secretItem, Serializer.Deserialize<SyncItemDto>(deferredItem.CanonicalJson.Span));
    }

    [Fact]
    public void ApplyRemoteChanges_applies_secret_item_when_crypto_profile_exists()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.SyncMetadataStore.UpsertDeferredSecretItem(new SyncDeferredSecretItemRecord(
            RemoteItemId: "remote-secret",
            SecretGenerationId: "generation",
            RemoteEtag: "old-etag",
            ContentHash: "sha256:old",
            CanonicalJson: DeferredJson,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "generation");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, secretItem.Id, secretItem)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.SyncMetadataStore.LoadDeferredSecretItems());
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-secret");
        Assert.True(storedItem.Item.IsSecret);
        Assert.Null(storedItem.Item.Title);
        Assert.Null(storedItem.Item.Url);
        var payload = Assert.IsType<EncryptedBookmarkPayloadRecord>(storedItem.Item.EncryptedPayload);
        Assert.Equal(new byte[] { 10, 11, 12 }, payload.Payload.ToArray());
        Assert.Equal(new byte[] { 13, 14, 15 }, payload.Nonce.ToArray());
        Assert.Equal(SecretCryptoProfileIds.ActiveProfileId, payload.CryptoProfileId);
        Assert.Equal(1, payload.PayloadFormatVersion);
        Assert.Equal("sha256:remote-secret-content", storedItem.SyncMetadata.ContentHash);
    }

    [Fact]
    public void ApplyRemoteChanges_defers_missing_secret_icon_reference()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "generation") with
        {
            SecretIconAssetRef = new SyncAssetReferenceDto(
                AssetId: "missing-secret-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "missing-secret-source-hash")
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, secretItem.Id, secretItem)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-secret");
        Assert.True(storedItem.Item.IsSecret);
        Assert.Null(storedItem.Item.SecretIconAssetId);
        var pendingRef = Assert.Single(database.SyncMetadataStore.LoadPendingAssetRefs());
        Assert.Equal("remote-secret", pendingRef.ItemId);
        Assert.Equal(SyncPendingAssetKind.SecretIcon, pendingRef.AssetKind);
        Assert.Equal("missing-secret-icon", pendingRef.RemoteAssetId);
        Assert.Equal("sha256", pendingRef.SourceHashAlgorithm);
        Assert.Equal("missing-secret-source-hash", pendingRef.SourceHash);
    }

    [Fact]
    public void ApplyRemoteChanges_resolves_pending_secret_icon_reference_after_secret_icon_arrives()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "generation") with
        {
            SecretIconAssetRef = new SyncAssetReferenceDto(
                AssetId: "remote-secret-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "remote-secret-source-hash")
        };
        var firstBatch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, secretItem.Id, secretItem)]);
        var secretIconAsset = CreateRemoteSecretIconAsset("remote-secret-icon", "generation");
        var secondBatch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [Applied(SyncObjectKind.SecretIconAsset, secretIconAsset.Id, secretIconAsset)],
            Items: []);

        database.SyncLocalStore.ApplyRemoteChanges(firstBatch);
        database.SyncLocalStore.ApplyRemoteChanges(secondBatch);

        Assert.Empty(database.SyncMetadataStore.LoadPendingAssetRefs());
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-secret");
        Assert.Equal("remote-secret-icon", storedItem.Item.SecretIconAssetId);
        Assert.Equal(BookmarkSyncState.Clean, storedItem.SyncMetadata.SyncState);
    }

    [Fact]
    public void ApplyRemoteChanges_uses_existing_secret_icon_reference_and_clears_pending_ref()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.InsertSecretIconAsset("existing-secret-icon", "generation");
        database.InsertSecretBookmark("remote-secret", "generation");
        database.SyncMetadataStore.UpsertPendingAssetRef(new SyncPendingAssetRefRecord(
            Id: "old-pending",
            ItemId: "remote-secret",
            AssetKind: SyncPendingAssetKind.SecretIcon,
            RemoteAssetId: "old-missing-secret-icon",
            SourceHashAlgorithm: null,
            SourceHash: null,
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "generation") with
        {
            SecretIconAssetRef = new SyncAssetReferenceDto(
                AssetId: "existing-secret-icon",
                SourceHashAlgorithm: "sha256",
                SourceHash: "secret-source-hash")
        };
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [],
            IconAssets: [],
            SecretIconAssets: [],
            Items: [Applied(SyncObjectKind.Item, secretItem.Id, secretItem)]);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-secret");
        Assert.Equal("existing-secret-icon", storedItem.Item.SecretIconAssetId);
        Assert.Empty(database.SyncMetadataStore.LoadPendingAssetRefs());
    }

    [Fact]
    public void ApplyRemoteChanges_applies_deferred_secret_item_after_crypto_profile_arrives()
    {
        using var database = TempSqliteDatabase.Create();
        var secretItem = CreateRemoteSecretBookmark("remote-secret", "remote-generation");
        database.SyncMetadataStore.UpsertDeferredSecretItem(new SyncDeferredSecretItemRecord(
            RemoteItemId: "remote-secret",
            SecretGenerationId: "remote-generation",
            RemoteEtag: "secret-etag",
            ContentHash: "sha256:remote-secret-content",
            CanonicalJson: Serializer.Serialize(secretItem),
            CreatedAtUtc: Now,
            LastAttemptAtUtc: null,
            AttemptCount: 0,
            LastErrorCode: null));
        var profile = CreateRemoteCryptoProfile("remote-generation");
        var batch = new SyncApplyBatch(
            SecretResetEvents: [],
            CryptoProfiles: [Applied(SyncObjectKind.CryptoProfile, profile.SecretGenerationId, profile)],
            IconAssets: [],
            SecretIconAssets: [],
            Items: []);

        database.SyncLocalStore.ApplyRemoteChanges(batch);

        Assert.Empty(database.SyncMetadataStore.LoadDeferredSecretItems());
        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "remote-secret");
        Assert.True(storedItem.Item.IsSecret);
        Assert.Equal("secret-etag", storedItem.SyncMetadata.RemoteEtag);
        Assert.Equal("sha256:remote-secret-content", storedItem.SyncMetadata.ContentHash);
    }

    [Fact]
    public void MarkUploaded_marks_supported_dirty_objects_as_clean()
    {
        using var database = TempSqliteDatabase.Create();
        database.ProfileStore.SaveNewProfile(CreateProfile("generation"));
        database.ResetStore.ResetMasterPasswordAndPurgeSecrets("generation");
        database.ProfileStore.SaveNewProfile(CreateProfile("active-generation"));
        database.InsertBookmark("bookmark");
        database.InsertIconAsset("icon");
        database.InsertSecretIconAsset("secret-icon", "active-generation");

        database.SyncLocalStore.MarkUploaded(
            new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"),
            "item-etag",
            "sha256:item-uploaded",
            Now);
        database.SyncLocalStore.MarkUploaded(
            new SyncObjectIdentity(SyncObjectKind.IconAsset, "icon"),
            "icon-etag",
            "sha256:icon-uploaded",
            Now);
        database.SyncLocalStore.MarkUploaded(
            new SyncObjectIdentity(SyncObjectKind.SecretIconAsset, "secret-icon"),
            "secret-icon-etag",
            "sha256:secret-icon-uploaded",
            Now);
        database.SyncLocalStore.MarkUploaded(
            new SyncObjectIdentity(SyncObjectKind.CryptoProfile, "active-generation"),
            "profile-etag",
            "sha256:profile-uploaded",
            Now);
        database.SyncLocalStore.MarkUploaded(
            new SyncObjectIdentity(SyncObjectKind.SecretResetEvent, "generation"),
            "reset-etag",
            "sha256:reset-uploaded",
            Now);

        var snapshot = database.SyncLocalStore.LoadSnapshot();
        Assert.Contains(snapshot.Items, item =>
            item.Item.Id == "bookmark" &&
            item.SyncMetadata.SyncState == BookmarkSyncState.Clean &&
            item.SyncMetadata.RemoteEtag == "item-etag" &&
            item.SyncMetadata.ContentHash == "sha256:item-uploaded");
        Assert.Contains(snapshot.IconAssets, icon =>
            icon.Asset.Id == "icon" &&
            icon.SyncMetadata.SyncState == BookmarkSyncState.Clean &&
            icon.SyncMetadata.RemoteEtag == "icon-etag" &&
            icon.SyncMetadata.ContentHash == "sha256:icon-uploaded");
        Assert.Contains(snapshot.SecretIconAssets, icon =>
            icon.Asset.Id == "secret-icon" &&
            icon.SyncMetadata.SyncState == BookmarkSyncState.Clean &&
            icon.SyncMetadata.RemoteEtag == "secret-icon-etag" &&
            icon.SyncMetadata.ContentHash == "sha256:secret-icon-uploaded");
        Assert.Contains(snapshot.CryptoProfiles, profile =>
            profile.Profile.SecretGenerationId == "active-generation" &&
            profile.SyncMetadata.SyncState == BookmarkSyncState.Clean &&
            profile.SyncMetadata.RemoteEtag == "profile-etag" &&
            profile.SyncMetadata.ContentHash == "sha256:profile-uploaded");
        Assert.Contains(snapshot.SecretResetEvents, resetEvent =>
            resetEvent.SecretGenerationId == "generation" &&
            resetEvent.SyncState == BookmarkSyncState.Clean &&
            resetEvent.RemoteEtag == "reset-etag");
    }

    [Fact]
    public void MarkConflict_marks_item_conflict_without_clearing_existing_sync_metadata()
    {
        using var database = TempSqliteDatabase.Create();
        database.InsertBookmark("bookmark");

        database.SyncLocalStore.MarkConflict(
            new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"),
            "local-dirty-remote-changed");

        var storedItem = Assert.Single(database.SyncLocalStore.LoadSnapshot().Items, item => item.Item.Id == "bookmark");
        Assert.Equal(BookmarkSyncState.Conflict, storedItem.SyncMetadata.SyncState);
        Assert.Equal("sha256:item-content", storedItem.SyncMetadata.ContentHash);
        Assert.Equal("test-device", storedItem.SyncMetadata.ModifiedDeviceId);
    }

    private static CryptoProfileRecord CreateProfile(string secretGenerationId)
    {
        return new(
            SecretCryptoProfileIds.ActiveProfileId,
            SecretEncryptionConstants.CurrentProfileVersion,
            SecretEncryptionConstants.KdfName,
            SecretEncryptionConstants.KdfHashAlgorithm,
            1000,
            Enumerable.Repeat((byte)1, SecretEncryptionConstants.KdfSaltLengthBytes).ToArray(),
            SecretEncryptionConstants.KekLengthBytes,
            SecretEncryptionConstants.DataKeyAlgorithm,
            WrappedDataKey,
            WrappedDataKeyNonce,
            SecretEncryptionConstants.EncryptionAlgorithm,
            SecretEncryptionConstants.PayloadFormat,
            PasswordCheckPayload,
            PasswordCheckNonce,
            Now,
            Now,
            secretGenerationId);
    }

    private static SyncIconAssetDto CreateRemoteIconAsset(string id)
    {
        return new SyncIconAssetDto(
            SyncRemoteObjectConstants.IconAssetSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            "sha256",
            "remote-source-hash",
            3,
            "image/png",
            32,
            32,
            Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            Now,
            Now,
            "remote-device",
            "sha256:remote-icon-content");
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
            3,
            "image/png",
            32,
            32,
            Convert.ToBase64String(new byte[] { 4, 5, 6 }),
            Convert.ToBase64String(new byte[] { 7, 8, 9 }),
            PayloadFormatVersion: 1,
            SecretGenerationId: secretGenerationId,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            ModifiedDeviceId: "remote-device",
            ContentHash: "sha256:remote-secret-icon-content");
    }

    private static SyncCryptoProfileDto CreateRemoteCryptoProfile(string secretGenerationId)
    {
        return new SyncCryptoProfileDto(
            SyncRemoteObjectConstants.CryptoProfileSchema,
            SyncRemoteObjectConstants.FormatVersion,
            secretGenerationId,
            SecretEncryptionConstants.CurrentProfileVersion,
            SecretEncryptionConstants.KdfName,
            1000,
            Convert.ToBase64String(Enumerable.Repeat((byte)1, SecretEncryptionConstants.KdfSaltLengthBytes).ToArray()),
            Convert.ToBase64String(WrappedDataKey),
            Convert.ToBase64String(WrappedDataKeyNonce),
            Convert.ToBase64String(PasswordCheckPayload),
            Convert.ToBase64String(PasswordCheckNonce),
            Now,
            Now,
            "remote-device",
            "sha256:remote-profile-content");
    }

    private static SyncSecretResetEventDto CreateRemoteResetEvent(string secretGenerationId)
    {
        return new SyncSecretResetEventDto(
            SyncRemoteObjectConstants.SecretResetEventSchema,
            SyncRemoteObjectConstants.FormatVersion,
            secretGenerationId,
            Now,
            "remote-device",
            1,
            Now,
            Now,
            "remote-device",
            "sha256:remote-reset-content");
    }

    private static SyncItemDto CreateRemoteFolder(string id)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            ParentId: null,
            SyncRemoteObjectConstants.FolderKind,
            SortOrder: 1000,
            Title: "Remote folder",
            Url: null,
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
            ContentHash: $"sha256:{id}-content");
    }

    private static SyncItemDto CreateRemoteBookmark(string id, string? parentId)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            parentId,
            SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 900,
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
            ContentHash: $"sha256:{id}-content");
    }

    private static SyncItemDto CreateRemoteSecretBookmark(string id, string secretGenerationId)
    {
        return new SyncItemDto(
            SyncRemoteObjectConstants.ItemSchema,
            SyncRemoteObjectConstants.FormatVersion,
            id,
            ParentId: null,
            SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 800,
            Title: null,
            Url: null,
            IsSecret: true,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: Convert.ToBase64String(new byte[] { 10, 11, 12 }),
            EncryptionNonce: Convert.ToBase64String(new byte[] { 13, 14, 15 }),
            CryptoProfileSecretGenerationId: secretGenerationId,
            SecretPayloadFormatVersion: 1,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "remote-device",
            ContentHash: "sha256:remote-secret-content");
    }

    private static SyncAppliedRemoteObject<T> Applied<T>(
        SyncObjectKind kind,
        string id,
        T value)
    {
        var identity = new SyncObjectIdentity(kind, id);
        return new SyncAppliedRemoteObject<T>(
            new SyncRemoteObjectInfo(
                SyncRemoteObjectPath.ToRelativePath(identity),
                "remote-etag",
                LastModifiedUtc: null,
                ContentLength: null),
            identity,
            value);
    }

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;
        private readonly SqliteConnectionFactory _connectionFactory;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            _connectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
            var idCallCount = 0;
            new SqliteDatabaseMigrator(
                _connectionFactory,
                idFactory: () => ++idCallCount switch
                {
                    1 => "database-id",
                    2 => "device-id",
                    _ => "unused-id"
                }).Migrate();
            ProfileStore = new SqliteSecretProfileStore(_connectionFactory);
            ResetStore = new SqliteSecretResetStore(
                _connectionFactory,
                idFactory: () => "reset-event",
                clock: () => Now,
                resetDeviceId: "test-device");
            SyncMetadataStore = new SqliteSyncMetadataStore(_connectionFactory);
            TreeStore = new SqliteBookmarkTreeStore(
                _connectionFactory,
                clock: () => Now,
                modifiedDeviceId: "test-device");
            SyncLocalStore = new SqliteSyncLocalStore(
                TreeStore,
                ProfileStore,
                ResetStore,
                SyncMetadataStore,
                Serializer);
        }

        public SqliteBookmarkTreeStore TreeStore { get; }

        public SqliteSecretProfileStore ProfileStore { get; }

        public SqliteSecretResetStore ResetStore { get; }

        public SqliteSyncMetadataStore SyncMetadataStore { get; }

        public SqliteSyncLocalStore SyncLocalStore { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public void InsertFolder(string id)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items (
                    id,
                    parent_id,
                    item_type,
                    sort_order,
                    title,
                    url,
                    is_secret,
                    encrypted_payload,
                    encryption_nonce,
                    crypto_profile_id,
                    secret_payload_format_version,
                    created_at_utc,
                    updated_at_utc,
                    deleted_at_utc,
                    revision,
                    content_hash,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    modified_device_id)
                VALUES (
                    $id,
                    NULL,
                    'folder',
                    1100,
                    'Folder',
                    NULL,
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    '2026-01-01T00:00:00.0000000Z',
                    '2026-01-01T00:00:00.0000000Z',
                    NULL,
                    1,
                    'sha256:folder-content',
                    'dirty',
                    NULL,
                    NULL,
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        public void InsertBookmark(string id, string? parentId = null)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items (
                    id,
                    parent_id,
                    item_type,
                    sort_order,
                    title,
                    url,
                    is_secret,
                    encrypted_payload,
                    encryption_nonce,
                    crypto_profile_id,
                    secret_payload_format_version,
                    created_at_utc,
                    updated_at_utc,
                    deleted_at_utc,
                    revision,
                    content_hash,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    modified_device_id)
                VALUES (
                    $id,
                    $parentId,
                    'bookmark',
                    1000,
                    'Title',
                    'https://example.com',
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    '2026-01-01T00:00:00.0000000Z',
                    '2026-01-01T00:00:00.0000000Z',
                    NULL,
                    1,
                    'sha256:item-content',
                    'dirty',
                    NULL,
                    NULL,
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$parentId", parentId is null ? DBNull.Value : parentId);
            command.ExecuteNonQuery();
        }

        public void InsertDeletedBookmark(string id)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items (
                    id,
                    parent_id,
                    item_type,
                    sort_order,
                    title,
                    url,
                    is_secret,
                    encrypted_payload,
                    encryption_nonce,
                    crypto_profile_id,
                    secret_payload_format_version,
                    created_at_utc,
                    updated_at_utc,
                    deleted_at_utc,
                    revision,
                    content_hash,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    modified_device_id)
                VALUES (
                    $id,
                    NULL,
                    'bookmark',
                    900,
                    'Deleted',
                    'https://example.com/deleted',
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    '2026-01-01T00:00:00.0000000Z',
                    '2026-01-02T00:00:00.0000000Z',
                    '2026-01-02T00:00:00.0000000Z',
                    2,
                    'sha256:deleted-item-content',
                    'dirty',
                    NULL,
                    NULL,
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        public void InsertSecretOnlyFolder(string id)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items (
                    id,
                    parent_id,
                    item_type,
                    sort_order,
                    title,
                    url,
                    is_secret,
                    encrypted_payload,
                    encryption_nonce,
                    crypto_profile_id,
                    secret_payload_format_version,
                    created_at_utc,
                    updated_at_utc,
                    deleted_at_utc,
                    revision,
                    content_hash,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    modified_device_id)
                VALUES (
                    $id,
                    NULL,
                    'folder',
                    800,
                    'Secret folder',
                    NULL,
                    0,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    '2026-01-01T00:00:00.0000000Z',
                    '2026-01-01T00:00:00.0000000Z',
                    NULL,
                    1,
                    'sha256:secret-folder-content',
                    'dirty',
                    NULL,
                    NULL,
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        public void InsertSecretBookmark(
            string id,
            string secretGenerationId,
            string? parentId = null)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items (
                    id,
                    parent_id,
                    item_type,
                    sort_order,
                    title,
                    url,
                    is_secret,
                    encrypted_payload,
                    encryption_nonce,
                    crypto_profile_id,
                    secret_payload_format_version,
                    created_at_utc,
                    updated_at_utc,
                    deleted_at_utc,
                    revision,
                    content_hash,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    modified_device_id)
                VALUES (
                    $id,
                    $parentId,
                    'bookmark',
                    700,
                    NULL,
                    NULL,
                    1,
                    $encryptedPayload,
                    $encryptedNonce,
                    (
                        SELECT id
                        FROM crypto_profiles
                        WHERE secret_generation_id = $secretGenerationId
                    ),
                    1,
                    '2026-01-01T00:00:00.0000000Z',
                    '2026-01-01T00:00:00.0000000Z',
                    NULL,
                    1,
                    'sha256:secret-item-content',
                    'dirty',
                    NULL,
                    NULL,
                    'test-device');
            """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$parentId", parentId is null ? DBNull.Value : parentId);
            command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
            command.Parameters.AddWithValue("$encryptedPayload", WrappedDataKey);
            command.Parameters.AddWithValue("$encryptedNonce", WrappedDataKeyNonce);
            command.ExecuteNonQuery();
        }

        public void InsertIconAsset(string id)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO icon_assets (
                    id,
                    source_hash_algorithm,
                    source_hash,
                    source_size_bytes,
                    processed_mime_type,
                    processed_width,
                    processed_height,
                    processed_bytes,
                    created_at_utc,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    content_hash,
                    modified_device_id)
                VALUES (
                    $id,
                    'sha256',
                    'source-hash',
                    3,
                    'image/png',
                    32,
                    32,
                    $processedBytes,
                    '2026-01-01T00:00:00.0000000Z',
                    'dirty',
                    NULL,
                    NULL,
                    'sha256:icon-content',
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$processedBytes", new byte[] { 1, 2, 3 });
            command.ExecuteNonQuery();
        }

        public void InsertSecretIconAsset(string id, string secretGenerationId)
        {
            using var connection = _connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO secret_icon_assets (
                    id,
                    source_hash_algorithm,
                    source_hash,
                    source_size_bytes,
                    processed_mime_type,
                    processed_width,
                    processed_height,
                    encrypted_processed_bytes,
                    encryption_nonce,
                    payload_format_version,
                    secret_generation_id,
                    created_at_utc,
                    sync_state,
                    remote_etag,
                    last_synced_at_utc,
                    content_hash,
                    modified_device_id)
                VALUES (
                    $id,
                    'sha256',
                    'secret-source-hash',
                    3,
                    'image/png',
                    32,
                    32,
                    $encryptedProcessedBytes,
                    $encryptionNonce,
                    1,
                    $secretGenerationId,
                    '2026-01-01T00:00:00.0000000Z',
                    'dirty',
                    NULL,
                    NULL,
                    'sha256:secret-icon-content',
                    'test-device');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
            command.Parameters.AddWithValue("$encryptedProcessedBytes", new byte[] { 4, 5, 6 });
            command.Parameters.AddWithValue("$encryptionNonce", new byte[] { 7, 8, 9 });
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                    Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static readonly byte[] WrappedDataKey = [1, 2, 3];

    private static readonly byte[] WrappedDataKeyNonce = [4, 5, 6];

    private static readonly byte[] PasswordCheckPayload = [7, 8, 9];

    private static readonly byte[] PasswordCheckNonce = [10, 11, 12];

    private static readonly byte[] DeferredJson = [13, 14, 15];

    private static readonly SystemTextSyncJsonSerializer Serializer = new();

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
