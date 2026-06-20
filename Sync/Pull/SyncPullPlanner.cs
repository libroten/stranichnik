using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;

namespace Stranichnik.Sync.Pull;

public sealed class SyncPullPlanner
{
    private const string DirtyRemoteChangedReason = "local-dirty-remote-changed";
    private const string IdentityMismatchReason = "identity-mismatch";

    public static SyncPullPlan Plan(
        SyncLocalSnapshot snapshot,
        IReadOnlyList<SyncRemoteReadResult<SyncSecretResetEventDto>> secretResetEvents,
        IReadOnlyList<SyncRemoteReadResult<SyncCryptoProfileDto>> cryptoProfiles,
        IReadOnlyList<SyncRemoteReadResult<SyncIconAssetDto>> iconAssets,
        IReadOnlyList<SyncRemoteReadResult<SyncSecretIconAssetDto>> secretIconAssets,
        IReadOnlyList<SyncRemoteReadResult<SyncItemDto>> items)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(secretResetEvents);
        ArgumentNullException.ThrowIfNull(cryptoProfiles);
        ArgumentNullException.ThrowIfNull(iconAssets);
        ArgumentNullException.ThrowIfNull(secretIconAssets);
        ArgumentNullException.ThrowIfNull(items);

        var context = new PlanningContext(snapshot.QuarantinedRemoteObjects);

        var resetsToApply = PlanResetEvents(snapshot, secretResetEvents, context);
        var resetGenerationIds = snapshot.SecretResetEvents
            .Select(resetEvent => resetEvent.SecretGenerationId)
            .Concat(resetsToApply.Select(resetEvent => resetEvent.Value.SecretGenerationId))
            .ToHashSet(StringComparer.Ordinal);
        var profilesToApply = PlanObjects(
            cryptoProfiles,
            snapshot.CryptoProfiles.ToDictionary(
                profile => profile.Profile.SecretGenerationId,
                profile => profile.SyncMetadata,
                StringComparer.Ordinal),
            SyncObjectKind.CryptoProfile,
            profile => profile.SecretGenerationId,
            profile => profile.ContentHash,
            context)
            .Where(profile => !resetGenerationIds.Contains(profile.Value.SecretGenerationId))
            .ToList();
        var iconAssetsToApply = PlanObjects(
            iconAssets,
            snapshot.IconAssets.ToDictionary(
                asset => asset.Asset.Id,
                asset => asset.SyncMetadata,
                StringComparer.Ordinal),
            SyncObjectKind.IconAsset,
            asset => asset.Id,
            asset => asset.ContentHash,
            context);
        var secretIconAssetsToApply = PlanObjects(
            secretIconAssets,
            snapshot.SecretIconAssets.ToDictionary(
                asset => asset.Asset.Id,
                asset => asset.SyncMetadata,
                StringComparer.Ordinal),
            SyncObjectKind.SecretIconAsset,
            asset => asset.Id,
            asset => asset.ContentHash,
            context)
            .Where(asset => !resetGenerationIds.Contains(asset.Value.SecretGenerationId))
            .ToList();
        var itemsToApply = PlanObjects(
            items,
            snapshot.Items.ToDictionary(
                item => item.Item.Id,
                item => item.SyncMetadata,
                StringComparer.Ordinal),
            SyncObjectKind.Item,
            item => item.Id,
            item => item.ContentHash,
            context)
            .Where(item => !IsResetSecretItem(item.Value, resetGenerationIds))
            .ToList();
        var missingRemoteObjects = PlanMissingRemoteObjects(
            snapshot,
            secretResetEvents,
            cryptoProfiles,
            iconAssets,
            secretIconAssets,
            items,
            resetGenerationIds);

        return new SyncPullPlan(
            new SyncApplyBatch(
                resetsToApply,
                profilesToApply,
                iconAssetsToApply,
                secretIconAssetsToApply,
                itemsToApply),
            context.Conflicts.ToArray(),
            context.MatchedDirtyObjects.ToArray(),
            context.QuarantinedRemoteObjects.ToArray(),
            context.KnownQuarantinedRemoteObjects.ToArray(),
            context.ResolvedQuarantinedRemoteObjectIds.ToArray(),
            missingRemoteObjects);
    }

    private static List<SyncAppliedRemoteObject<SyncSecretResetEventDto>> PlanResetEvents(
        SyncLocalSnapshot snapshot,
        IReadOnlyList<SyncRemoteReadResult<SyncSecretResetEventDto>> remoteResults,
        PlanningContext context)
    {
        var localResetGenerationIds = snapshot.SecretResetEvents
            .Select(resetEvent => resetEvent.SecretGenerationId)
            .ToHashSet(StringComparer.Ordinal);
        var resetEventsToApply = new List<SyncAppliedRemoteObject<SyncSecretResetEventDto>>();

        foreach (var result in remoteResults)
        {
            if (!TryGetSuccessfulValue(
                result,
                SyncObjectKind.SecretResetEvent,
                resetEvent => resetEvent.SecretGenerationId,
                resetEvent => resetEvent.ContentHash,
                context,
                out var value))
            {
                continue;
            }

            if (!localResetGenerationIds.Contains(value.SecretGenerationId))
            {
                var identity = result.Identity
                    ?? throw new InvalidOperationException("Successful remote read result must have an identity.");

                resetEventsToApply.Add(new SyncAppliedRemoteObject<SyncSecretResetEventDto>(
                    result.RemoteInfo,
                    identity,
                    value));
            }
        }

        return resetEventsToApply;
    }

    private static bool IsResetSecretItem(
        SyncItemDto item,
        HashSet<string> resetGenerationIds)
    {
        return item.IsSecret &&
            item.CryptoProfileSecretGenerationId is not null &&
            resetGenerationIds.Contains(item.CryptoProfileSecretGenerationId);
    }

    private static List<SyncAppliedRemoteObject<T>> PlanObjects<T>(
        IReadOnlyList<SyncRemoteReadResult<T>> remoteResults,
        Dictionary<string, SyncObjectMetadata> localMetadataById,
        SyncObjectKind kind,
        Func<T, string> getId,
        Func<T, string> getContentHash,
        PlanningContext context)
    {
        var objectsToApply = new List<SyncAppliedRemoteObject<T>>();

        foreach (var result in remoteResults)
        {
            if (!TryGetSuccessfulValue(
                result,
                kind,
                getId,
                getContentHash,
                context,
                out var value))
            {
                continue;
            }

            var remoteContentHash = getContentHash(value);
            var identity = result.Identity
                ?? throw new InvalidOperationException("Successful remote read result must have an identity.");

            if (!localMetadataById.TryGetValue(identity.Id, out var localMetadata))
            {
                objectsToApply.Add(new SyncAppliedRemoteObject<T>(
                    result.RemoteInfo,
                    identity,
                    value));
                continue;
            }

            if (localMetadata.SyncState == BookmarkSyncState.Dirty)
            {
                if (string.Equals(localMetadata.ContentHash, remoteContentHash, StringComparison.Ordinal))
                {
                    context.MatchedDirtyObjects.Add(new SyncPullMatchedDirtyObject(
                        identity,
                        result.RemoteInfo.ETag,
                        remoteContentHash));
                }
                else
                {
                    context.Conflicts.Add(new SyncPullConflict(
                        identity,
                        DirtyRemoteChangedReason,
                        localMetadata.ContentHash,
                        remoteContentHash));
                }

                continue;
            }

            if (localMetadata.SyncState == BookmarkSyncState.Conflict)
            {
                if (string.Equals(localMetadata.ContentHash, remoteContentHash, StringComparison.Ordinal))
                {
                    context.MatchedDirtyObjects.Add(new SyncPullMatchedDirtyObject(
                        identity,
                        result.RemoteInfo.ETag,
                        remoteContentHash));
                }

                continue;
            }

            if (!string.Equals(localMetadata.ContentHash, remoteContentHash, StringComparison.Ordinal))
            {
                objectsToApply.Add(new SyncAppliedRemoteObject<T>(
                    result.RemoteInfo,
                    identity,
                    value));
            }
        }

        return objectsToApply;
    }

    private static List<SyncObjectIdentity> PlanMissingRemoteObjects(
        SyncLocalSnapshot snapshot,
        IReadOnlyList<SyncRemoteReadResult<SyncSecretResetEventDto>> secretResetEvents,
        IReadOnlyList<SyncRemoteReadResult<SyncCryptoProfileDto>> cryptoProfiles,
        IReadOnlyList<SyncRemoteReadResult<SyncIconAssetDto>> iconAssets,
        IReadOnlyList<SyncRemoteReadResult<SyncSecretIconAssetDto>> secretIconAssets,
        IReadOnlyList<SyncRemoteReadResult<SyncItemDto>> items,
        HashSet<string> resetGenerationIds)
    {
        var missingObjects = new List<SyncObjectIdentity>();

        AddMissingRemoteObjects(
            snapshot.SecretResetEvents.Select(resetEvent => (
                Id: resetEvent.SecretGenerationId,
                Metadata: new SyncObjectMetadata(
                    resetEvent.SyncState,
                    resetEvent.RemoteEtag,
                    resetEvent.LastSyncedAtUtc,
                    ContentHash: null,
                    ModifiedDeviceId: resetEvent.ResetDeviceId))),
            secretResetEvents,
            SyncObjectKind.SecretResetEvent,
            missingObjects);
        AddMissingRemoteObjects(
            snapshot.CryptoProfiles
                .Where(profile => !resetGenerationIds.Contains(profile.Profile.SecretGenerationId))
                .Select(profile => (
                    Id: profile.Profile.SecretGenerationId,
                    profile.SyncMetadata)),
            cryptoProfiles,
            SyncObjectKind.CryptoProfile,
            missingObjects);
        AddMissingRemoteObjects(
            snapshot.IconAssets.Select(asset => (
                Id: asset.Asset.Id,
                asset.SyncMetadata)),
            iconAssets,
            SyncObjectKind.IconAsset,
            missingObjects);
        AddMissingRemoteObjects(
            snapshot.SecretIconAssets
                .Where(asset => !resetGenerationIds.Contains(asset.Asset.SecretGenerationId))
                .Select(asset => (
                    Id: asset.Asset.Id,
                    asset.SyncMetadata)),
            secretIconAssets,
            SyncObjectKind.SecretIconAsset,
            missingObjects);
        AddMissingRemoteObjects(
            snapshot.Items
                .Where(item => !IsResetSecretItem(item.Item, snapshot.CryptoProfiles, resetGenerationIds))
                .Select(item => (
                    Id: item.Item.Id,
                    item.SyncMetadata)),
            items,
            SyncObjectKind.Item,
            missingObjects);

        return missingObjects;
    }

    private static void AddMissingRemoteObjects<T>(
        IEnumerable<(string Id, SyncObjectMetadata Metadata)> localObjects,
        IReadOnlyList<SyncRemoteReadResult<T>> remoteResults,
        SyncObjectKind kind,
        List<SyncObjectIdentity> missingObjects)
    {
        var remoteIds = remoteResults
            .Where(result => result.Identity?.Kind == kind)
            .Select(result => result.Identity!.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (id, metadata) in localObjects)
        {
            if (metadata is { SyncState: BookmarkSyncState.Clean, LastSyncedAtUtc: not null } &&
                !remoteIds.Contains(id))
            {
                missingObjects.Add(new SyncObjectIdentity(kind, id));
            }
        }
    }

    private static bool TryGetSuccessfulValue<T>(
        SyncRemoteReadResult<T> result,
        SyncObjectKind expectedKind,
        Func<T, string> getId,
        Func<T, string> getContentHash,
        PlanningContext context,
        out T value)
    {
        value = default!;

        if (result.Status != SyncRemoteReadStatus.Success ||
            result.Value is null ||
            result.Identity is null)
        {
            context.MarkQuarantined(ToQuarantineCandidate(
                result,
                ToReasonCode(result.Status),
                result.ContentHash));
            return false;
        }

        if (result.Identity.Kind != expectedKind ||
            !string.Equals(result.Identity.Id, getId(result.Value), StringComparison.Ordinal))
        {
            context.MarkQuarantined(ToQuarantineCandidate(
                result,
                IdentityMismatchReason,
                getContentHash(result.Value)));
            return false;
        }

        context.MarkResolved(result);
        value = result.Value;
        return true;
    }

    private static SyncPullQuarantineCandidate ToQuarantineCandidate<T>(
        SyncRemoteReadResult<T> result,
        string reasonCode,
        string? contentHash)
    {
        return new SyncPullQuarantineCandidate(
            result.Identity?.Kind.ToString() ?? "unknown",
            result.RemoteInfo.RelativePath,
            result.RemoteInfo.ETag,
            contentHash,
            reasonCode);
    }

    private static bool IsResetSecretItem(
        BookmarkItemRecord item,
        IReadOnlyList<SyncCryptoProfileSnapshotRecord> cryptoProfiles,
        HashSet<string> resetGenerationIds)
    {
        if (!item.IsSecret ||
            item.EncryptedPayload is not EncryptedBookmarkPayloadRecord payload)
        {
            return false;
        }

        return cryptoProfiles.Any(profile =>
            profile.Profile.Id == payload.CryptoProfileId &&
            resetGenerationIds.Contains(profile.Profile.SecretGenerationId));
    }

    private static string ToReasonCode(SyncRemoteReadStatus status)
    {
        return status switch
        {
            SyncRemoteReadStatus.InvalidPath => "invalid-path",
            SyncRemoteReadStatus.MissingContent => "missing-content",
            SyncRemoteReadStatus.InvalidJson => "invalid-json",
            SyncRemoteReadStatus.InvalidRemoteObject => "invalid-remote-object",
            SyncRemoteReadStatus.Success => "unknown",
            _ => "unknown"
        };
    }

    private sealed class PlanningContext
    {
        private readonly Dictionary<string, SyncQuarantinedRemoteObjectRecord> _quarantinedObjectsById;

        public PlanningContext(IReadOnlyList<SyncQuarantinedRemoteObjectRecord> quarantinedObjects)
        {
            _quarantinedObjectsById = quarantinedObjects.ToDictionary(
                quarantine => quarantine.Id,
                StringComparer.Ordinal);
        }

        public List<SyncPullConflict> Conflicts { get; } = [];

        public List<SyncPullMatchedDirtyObject> MatchedDirtyObjects { get; } = [];

        public List<SyncPullQuarantineCandidate> QuarantinedRemoteObjects { get; } = [];

        public List<SyncPullQuarantineCandidate> KnownQuarantinedRemoteObjects { get; } = [];

        public List<string> ResolvedQuarantinedRemoteObjectIds { get; } = [];

        public void MarkQuarantined(SyncPullQuarantineCandidate candidate)
        {
            var id = SyncQuarantinedRemoteObjectId.FromRemotePath(candidate.RelativePath);
            if (_quarantinedObjectsById.TryGetValue(id, out var existing) &&
                IsSameRemoteRevision(existing, candidate))
            {
                KnownQuarantinedRemoteObjects.Add(candidate);
                return;
            }

            QuarantinedRemoteObjects.Add(candidate);
        }

        public void MarkResolved<T>(SyncRemoteReadResult<T> result)
        {
            var pathId = SyncQuarantinedRemoteObjectId.FromRemotePath(result.RemoteInfo.RelativePath);
            if (_quarantinedObjectsById.ContainsKey(pathId))
                ResolvedQuarantinedRemoteObjectIds.Add(pathId);

            if (result.Identity is null)
                return;

            var identityId = SyncQuarantinedRemoteObjectId.FromIdentity(result.Identity);
            if (_quarantinedObjectsById.ContainsKey(identityId))
                ResolvedQuarantinedRemoteObjectIds.Add(identityId);
        }

        private static bool IsSameRemoteRevision(
            SyncQuarantinedRemoteObjectRecord existing,
            SyncPullQuarantineCandidate candidate)
        {
            if (!string.Equals(existing.ReasonCode, candidate.ReasonCode, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrWhiteSpace(existing.ContentHash) ||
                !string.IsNullOrWhiteSpace(candidate.ContentHash))
            {
                return string.Equals(existing.ContentHash, candidate.ContentHash, StringComparison.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(existing.RemoteEtag) ||
                !string.IsNullOrWhiteSpace(candidate.RemoteEtag))
            {
                return string.Equals(existing.RemoteEtag, candidate.RemoteEtag, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
