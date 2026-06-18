using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;

namespace Stranichnik.Sync.Push;

public sealed class SyncPushPlanner
{
    public static SyncPushPlan Plan(SyncLocalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var resetGenerationIds = snapshot.SecretResetEvents
            .Select(resetEvent => resetEvent.SecretGenerationId)
            .ToHashSet(StringComparer.Ordinal);
        var profileGenerationById = snapshot.CryptoProfiles
            .ToDictionary(profile => profile.Profile.Id, profile => profile.Profile.SecretGenerationId);
        var profileIdByGeneration = snapshot.CryptoProfiles
            .ToDictionary(profile => profile.Profile.SecretGenerationId, profile => profile.Profile.Id, StringComparer.Ordinal);
        var plannedItems = ItemsToPush(snapshot.Items, profileGenerationById, resetGenerationIds);
        var requiredIconAssetIds = RequiredIconAssetIds(plannedItems);
        var requiredSecretIconAssetIds = RequiredSecretIconAssetIds(plannedItems);
        var plannedSecretIconAssets = SecretIconAssetsToPush(
            snapshot.SecretIconAssets,
            resetGenerationIds,
            requiredSecretIconAssetIds);
        var requiredCryptoProfileIds = RequiredCryptoProfileIds(
            plannedItems,
            plannedSecretIconAssets,
            profileIdByGeneration);

        return new SyncPushPlan(
            SecretResetEvents: ResetEventsToPush(snapshot.SecretResetEvents),
            CryptoProfiles: CryptoProfilesToPush(snapshot.CryptoProfiles, resetGenerationIds, requiredCryptoProfileIds),
            IconAssets: IconAssetsToPush(snapshot.IconAssets, requiredIconAssetIds),
            SecretIconAssets: plannedSecretIconAssets,
            Items: plannedItems);
    }

    private static List<SecretResetEventRecord> ResetEventsToPush(
        IReadOnlyList<SecretResetEventRecord> resetEvents)
    {
        return resetEvents
            .Where(ShouldPush)
            .OrderBy(resetEvent => resetEvent.ResetAtUtc)
            .ThenBy(resetEvent => resetEvent.SecretGenerationId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncCryptoProfileSnapshotRecord> CryptoProfilesToPush(
        IReadOnlyList<SyncCryptoProfileSnapshotRecord> profiles,
        HashSet<string> resetGenerationIds,
        HashSet<long> requiredCryptoProfileIds)
    {
        return profiles
            .Where(profile =>
                ShouldPush(profile.SyncMetadata) ||
                requiredCryptoProfileIds.Contains(profile.Profile.Id))
            .Where(profile => !resetGenerationIds.Contains(profile.Profile.SecretGenerationId))
            .OrderBy(profile => profile.Profile.UpdatedAtUtc)
            .ThenBy(profile => profile.Profile.SecretGenerationId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncIconAssetSnapshotRecord> IconAssetsToPush(
        IReadOnlyList<SyncIconAssetSnapshotRecord> iconAssets,
        HashSet<string> requiredIconAssetIds)
    {
        return iconAssets
            .Where(asset =>
                ShouldPush(asset.SyncMetadata) ||
                requiredIconAssetIds.Contains(asset.Asset.Id))
            .OrderBy(asset => asset.Asset.CreatedAtUtc)
            .ThenBy(asset => asset.Asset.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncSecretIconAssetSnapshotRecord> SecretIconAssetsToPush(
        IReadOnlyList<SyncSecretIconAssetSnapshotRecord> secretIconAssets,
        HashSet<string> resetGenerationIds,
        HashSet<string> requiredSecretIconAssetIds)
    {
        return secretIconAssets
            .Where(asset =>
                ShouldPush(asset.SyncMetadata) ||
                requiredSecretIconAssetIds.Contains(asset.Asset.Id))
            .Where(asset => !resetGenerationIds.Contains(asset.Asset.SecretGenerationId))
            .OrderBy(asset => asset.Asset.CreatedAtUtc)
            .ThenBy(asset => asset.Asset.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncItemSnapshotRecord> ItemsToPush(
        IReadOnlyList<SyncItemSnapshotRecord> items,
        Dictionary<long, string> profileGenerationById,
        HashSet<string> resetGenerationIds)
    {
        var itemsById = items.ToDictionary(item => item.Item.Id, StringComparer.Ordinal);
        var selectedIds = items
            .Where(item => ShouldPush(item.SyncMetadata))
            .Where(item => !IsResetSecretItem(item, profileGenerationById, resetGenerationIds))
            .Select(item => item.Item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var itemId in selectedIds.ToArray())
        {
            IncludeParentFolders(itemId, itemsById, selectedIds);
        }

        var selectedItems = items
            .Where(item => selectedIds.Contains(item.Item.Id))
            .ToList();

        return selectedItems
            .OrderBy(item => GetParentDepth(item.Item, itemsById))
            .ThenBy(item => item.Item.Metadata.UpdatedAtUtc)
            .ThenBy(item => item.Item.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void IncludeParentFolders(
        string itemId,
        Dictionary<string, SyncItemSnapshotRecord> itemsById,
        HashSet<string> selectedIds)
    {
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
        var currentId = itemId;

        while (itemsById.TryGetValue(currentId, out var item))
        {
            var parentId = item.Item.ParentId;
            if (parentId is null || !visitedIds.Add(parentId))
                return;

            if (!itemsById.TryGetValue(parentId, out var parent))
                return;

            if (parent.Item.Kind != BookmarkItemKind.Folder || parent.Item.Metadata.DeletedAtUtc is not null)
                return;

            selectedIds.Add(parent.Item.Id);
            currentId = parent.Item.Id;
        }
    }

    private static int GetParentDepth(
        BookmarkItemRecord item,
        Dictionary<string, SyncItemSnapshotRecord> itemsById)
    {
        var depth = 0;
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
        var parentId = item.ParentId;

        while (parentId is not null &&
            visitedIds.Add(parentId) &&
            itemsById.TryGetValue(parentId, out var parent))
        {
            depth++;
            parentId = parent.Item.ParentId;
        }

        return depth;
    }

    private static HashSet<string> RequiredIconAssetIds(
        IReadOnlyList<SyncItemSnapshotRecord> items)
    {
        return items
            .Select(item => item.Item.IconAssetId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> RequiredSecretIconAssetIds(
        IReadOnlyList<SyncItemSnapshotRecord> items)
    {
        return items
            .Select(item => item.Item.SecretIconAssetId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<long> RequiredCryptoProfileIds(
        IReadOnlyList<SyncItemSnapshotRecord> items,
        IReadOnlyList<SyncSecretIconAssetSnapshotRecord> secretIconAssets,
        Dictionary<string, long> profileIdByGeneration)
    {
        var profileIds = items
            .Select(item => item.Item.EncryptedPayload?.CryptoProfileId)
            .OfType<long>()
            .ToHashSet();

        foreach (var secretIconAsset in secretIconAssets)
        {
            if (profileIdByGeneration.TryGetValue(secretIconAsset.Asset.SecretGenerationId, out var profileId))
                profileIds.Add(profileId);
        }

        return profileIds;
    }

    private static bool IsResetSecretItem(
        SyncItemSnapshotRecord item,
        Dictionary<long, string> profileGenerationById,
        HashSet<string> resetGenerationIds)
    {
        if (!item.Item.IsSecret)
            return false;

        var cryptoProfileId = item.Item.EncryptedPayload?.CryptoProfileId;
        if (cryptoProfileId is null)
            return true;

        return !profileGenerationById.TryGetValue(cryptoProfileId.Value, out var secretGenerationId) ||
            resetGenerationIds.Contains(secretGenerationId);
    }

    private static bool ShouldPush(SyncObjectMetadata metadata)
    {
        return metadata.SyncState == BookmarkSyncState.Dirty ||
            metadata.LastSyncedAtUtc is null;
    }

    private static bool ShouldPush(SecretResetEventRecord resetEvent)
    {
        return resetEvent.SyncState == BookmarkSyncState.Dirty ||
            resetEvent.LastSyncedAtUtc is null;
    }
}
