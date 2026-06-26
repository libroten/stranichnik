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
        var profilesById = snapshot.CryptoProfiles
            .ToDictionary(profile => profile.Profile.Id);
        var conflictedGenerationIds = snapshot.CryptoProfiles
            .Where(profile => profile.SyncMetadata.SyncState == BookmarkSyncState.Conflict)
            .Select(profile => profile.Profile.SecretGenerationId)
            .ToHashSet(StringComparer.Ordinal);
        var plannedItems = ItemsToPush(snapshot.Items, profilesById, resetGenerationIds, conflictedGenerationIds);
        var plannedSecretIconAssets = SecretIconAssetsToPush(
            snapshot.SecretIconAssets,
            resetGenerationIds,
            conflictedGenerationIds);

        return new SyncPushPlan(
            SecretResetEvents: ResetEventsToPush(snapshot.SecretResetEvents),
            CryptoProfiles: CryptoProfilesToPush(snapshot.CryptoProfiles, resetGenerationIds),
            IconAssets: IconAssetsToPush(snapshot.IconAssets),
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
        HashSet<string> resetGenerationIds)
    {
        return profiles
            .Where(profile => ShouldPush(profile.SyncMetadata))
            .Where(profile => profile.SyncMetadata.SyncState != BookmarkSyncState.Conflict)
            .Where(profile => !resetGenerationIds.Contains(profile.Profile.SecretGenerationId))
            .OrderBy(profile => profile.Profile.UpdatedAtUtc)
            .ThenBy(profile => profile.Profile.SecretGenerationId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncIconAssetSnapshotRecord> IconAssetsToPush(
        IReadOnlyList<SyncIconAssetSnapshotRecord> iconAssets)
    {
        return iconAssets
            .Where(asset => ShouldPush(asset.SyncMetadata))
            .OrderBy(asset => asset.Asset.CreatedAtUtc)
            .ThenBy(asset => asset.Asset.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncSecretIconAssetSnapshotRecord> SecretIconAssetsToPush(
        IReadOnlyList<SyncSecretIconAssetSnapshotRecord> secretIconAssets,
        HashSet<string> resetGenerationIds,
        HashSet<string> conflictedGenerationIds)
    {
        return secretIconAssets
            .Where(asset => ShouldPush(asset.SyncMetadata))
            .Where(asset => !resetGenerationIds.Contains(asset.Asset.SecretGenerationId))
            .Where(asset => !conflictedGenerationIds.Contains(asset.Asset.SecretGenerationId))
            .OrderBy(asset => asset.Asset.CreatedAtUtc)
            .ThenBy(asset => asset.Asset.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SyncItemSnapshotRecord> ItemsToPush(
        IReadOnlyList<SyncItemSnapshotRecord> items,
        Dictionary<long, SyncCryptoProfileSnapshotRecord> profilesById,
        HashSet<string> resetGenerationIds,
        HashSet<string> conflictedGenerationIds)
    {
        var itemsById = items.ToDictionary(item => item.Item.Id, StringComparer.Ordinal);
        var selectedIds = items
            .Where(item => ShouldPush(item.SyncMetadata))
            .Where(item => !IsUnpushableSecretItem(item, profilesById, resetGenerationIds, conflictedGenerationIds))
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

            if (!ShouldPush(parent.SyncMetadata))
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

    private static bool IsUnpushableSecretItem(
        SyncItemSnapshotRecord item,
        Dictionary<long, SyncCryptoProfileSnapshotRecord> profilesById,
        HashSet<string> resetGenerationIds,
        HashSet<string> conflictedGenerationIds)
    {
        if (!item.Item.IsSecret)
            return false;

        var secretGenerationId = item.Item.SecretGenerationId;
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            return true;

        var cryptoProfileId = item.Item.EncryptedPayload?.CryptoProfileId;
        if (cryptoProfileId is null ||
            !profilesById.TryGetValue(cryptoProfileId.Value, out var profile))
        {
            return true;
        }

        return !string.Equals(profile.Profile.SecretGenerationId, secretGenerationId, StringComparison.Ordinal) ||
            resetGenerationIds.Contains(secretGenerationId) ||
            conflictedGenerationIds.Contains(secretGenerationId);
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
