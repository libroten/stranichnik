using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Sync.Push;

public sealed class SyncPushService
{
    private const string PreconditionFailedReasonCode = "push-precondition-failed";

    private readonly ISyncLocalStore _localStore;
    private readonly IWebDavSyncTransport _transport;
    private readonly ISyncJsonSerializer _serializer;
    private readonly SyncRemoteDtoMapper _dtoMapper;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;

    public SyncPushService(
        ISyncLocalStore localStore,
        IWebDavSyncTransport transport,
        ISyncJsonSerializer serializer,
        SyncRemoteDtoMapper dtoMapper,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(localStore);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(dtoMapper);

        _localStore = localStore;
        _transport = transport;
        _serializer = serializer;
        _dtoMapper = dtoMapper;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? Logs.Print;
    }

    public async Task<SyncRunSummary> PushAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock();
        _log("Sync push started.");

        var snapshot = _localStore.LoadSnapshot();
        _log(
            "Sync push local snapshot loaded. " +
            $"Items={snapshot.Items.Count}; " +
            $"IconAssets={snapshot.IconAssets.Count}; " +
            $"SecretIconAssets={snapshot.SecretIconAssets.Count}; " +
            $"CryptoProfiles={snapshot.CryptoProfiles.Count}; " +
            $"ResetEvents={snapshot.SecretResetEvents.Count}; " +
            $"PendingAssets={snapshot.PendingAssetRefs.Count}; " +
            $"DeferredSecretItems={snapshot.DeferredSecretItems.Count}; " +
            $"QuarantinedRemoteObjects={snapshot.QuarantinedRemoteObjects.Count}.");
        var plan = SyncPushPlanner.Plan(snapshot);
        _log(
            "Sync push plan created. " +
            $"ResetEvents={plan.SecretResetEvents.Count}; " +
            $"CryptoProfiles={plan.CryptoProfiles.Count}; " +
            $"IconAssets={plan.IconAssets.Count}; " +
            $"SecretIconAssets={plan.SecretIconAssets.Count}; " +
            $"Items={plan.Items.Count}.");
        var syncedAtUtc = _clock();
        var result = new PushResult();
        var blockedRegularIconAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedSecretIconAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedCryptoProfileIds = new HashSet<long>();

        await UploadResetEventsAsync(plan.SecretResetEvents, syncedAtUtc, result, cancellationToken)
            .ConfigureAwait(false);
        await UploadCryptoProfilesAsync(plan.CryptoProfiles, syncedAtUtc, result, blockedCryptoProfileIds, cancellationToken)
            .ConfigureAwait(false);
        await UploadIconAssetsAsync(plan.IconAssets, syncedAtUtc, result, blockedRegularIconAssetIds, cancellationToken)
            .ConfigureAwait(false);
        await UploadSecretIconAssetsAsync(plan.SecretIconAssets, syncedAtUtc, result, blockedSecretIconAssetIds, cancellationToken)
            .ConfigureAwait(false);

        var secretGenerationByProfileId = SyncRemoteDtoMapper.CreateSecretGenerationMap(snapshot.CryptoProfiles);
        var iconAssetsById = SyncRemoteDtoMapper.CreateIconAssetMap(snapshot.IconAssets);
        var secretIconAssetsById = SyncRemoteDtoMapper.CreateSecretIconAssetMap(snapshot.SecretIconAssets);
        await UploadItemsAsync(
                plan.Items,
                secretGenerationByProfileId,
                iconAssetsById,
                secretIconAssetsById,
                blockedRegularIconAssetIds,
                blockedSecretIconAssetIds,
                blockedCryptoProfileIds,
                syncedAtUtc,
                result,
                cancellationToken)
            .ConfigureAwait(false);

        var finishedSnapshot = _localStore.LoadSnapshot();
        var finishedAtUtc = _clock();
        var summary = new SyncRunSummary(
            Succeeded: result.ConflictCount == 0 && result.ErrorCount == 0,
            DownloadedCount: 0,
            UploadedCount: result.UploadedCount,
            ConflictCount: result.ConflictCount,
            PendingAssetCount: finishedSnapshot.PendingAssetRefs.Count,
            PendingCryptoProfileCount: finishedSnapshot.DeferredSecretItems.Count,
            InvalidRemoteObjectCount: finishedSnapshot.QuarantinedRemoteObjects.Count,
            ErrorCount: result.ErrorCount,
            BlockingReason: SyncBlockingReason.None,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: finishedAtUtc);

        _log(
            "Sync push finished. " +
            $"Succeeded={summary.Succeeded}; " +
            $"Uploaded={summary.UploadedCount}; " +
            $"Conflicts={summary.ConflictCount}; " +
            $"Errors={summary.ErrorCount}.");

        return summary;
    }

    private async Task UploadResetEventsAsync(
        IReadOnlyList<SecretResetEventRecord> resetEvents,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        CancellationToken cancellationToken)
    {
        _log($"Sync push upload category started. Kind={SyncObjectKind.SecretResetEvent}; Count={resetEvents.Count}.");
        foreach (var resetEvent in resetEvents)
        {
            var dto = _dtoMapper.ToDto(resetEvent);
            await UploadObjectAsync(
                    new SyncObjectIdentity(SyncObjectKind.SecretResetEvent, resetEvent.SecretGenerationId),
                    dto,
                    resetEvent.RemoteEtag,
                    dto.ContentHash,
                    syncedAtUtc,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        _log($"Sync push upload category finished. Kind={SyncObjectKind.SecretResetEvent}.");
    }

    private async Task UploadCryptoProfilesAsync(
        IReadOnlyList<SyncCryptoProfileSnapshotRecord> profiles,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        HashSet<long> blockedCryptoProfileIds,
        CancellationToken cancellationToken)
    {
        _log($"Sync push upload category started. Kind={SyncObjectKind.CryptoProfile}; Count={profiles.Count}.");
        foreach (var profile in profiles)
        {
            var dto = _dtoMapper.ToDto(profile);
            var uploaded = await UploadObjectAsync(
                    new SyncObjectIdentity(SyncObjectKind.CryptoProfile, profile.Profile.SecretGenerationId),
                    dto,
                    profile.SyncMetadata.RemoteEtag,
                    dto.ContentHash,
                    syncedAtUtc,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!uploaded)
                blockedCryptoProfileIds.Add(profile.Profile.Id);
        }
        _log(
            "Sync push upload category finished. " +
            $"Kind={SyncObjectKind.CryptoProfile}; " +
            $"BlockedDependencies={blockedCryptoProfileIds.Count}.");
    }

    private async Task UploadIconAssetsAsync(
        IReadOnlyList<SyncIconAssetSnapshotRecord> iconAssets,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        HashSet<string> blockedIconAssetIds,
        CancellationToken cancellationToken)
    {
        _log($"Sync push upload category started. Kind={SyncObjectKind.IconAsset}; Count={iconAssets.Count}.");
        foreach (var iconAsset in iconAssets)
        {
            var dto = _dtoMapper.ToDto(iconAsset);
            var uploaded = await UploadObjectAsync(
                    new SyncObjectIdentity(SyncObjectKind.IconAsset, iconAsset.Asset.Id),
                    dto,
                    iconAsset.SyncMetadata.RemoteEtag,
                    dto.ContentHash,
                    syncedAtUtc,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!uploaded)
                blockedIconAssetIds.Add(iconAsset.Asset.Id);
        }
        _log(
            "Sync push upload category finished. " +
            $"Kind={SyncObjectKind.IconAsset}; " +
            $"BlockedDependencies={blockedIconAssetIds.Count}.");
    }

    private async Task UploadSecretIconAssetsAsync(
        IReadOnlyList<SyncSecretIconAssetSnapshotRecord> secretIconAssets,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        HashSet<string> blockedSecretIconAssetIds,
        CancellationToken cancellationToken)
    {
        _log($"Sync push upload category started. Kind={SyncObjectKind.SecretIconAsset}; Count={secretIconAssets.Count}.");
        foreach (var secretIconAsset in secretIconAssets)
        {
            var dto = _dtoMapper.ToDto(secretIconAsset);
            var uploaded = await UploadObjectAsync(
                    new SyncObjectIdentity(SyncObjectKind.SecretIconAsset, secretIconAsset.Asset.Id),
                    dto,
                    secretIconAsset.SyncMetadata.RemoteEtag,
                    dto.ContentHash,
                    syncedAtUtc,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!uploaded)
                blockedSecretIconAssetIds.Add(secretIconAsset.Asset.Id);
        }
        _log(
            "Sync push upload category finished. " +
            $"Kind={SyncObjectKind.SecretIconAsset}; " +
            $"BlockedDependencies={blockedSecretIconAssetIds.Count}.");
    }

    private async Task UploadItemsAsync(
        IReadOnlyList<SyncItemSnapshotRecord> items,
        IReadOnlyDictionary<long, string> secretGenerationByProfileId,
        IReadOnlyDictionary<string, BookmarkIconAssetRecord> iconAssetsById,
        IReadOnlyDictionary<string, SecretIconAssetRecord> secretIconAssetsById,
        HashSet<string> blockedRegularIconAssetIds,
        HashSet<string> blockedSecretIconAssetIds,
        HashSet<long> blockedCryptoProfileIds,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        CancellationToken cancellationToken)
    {
        var plannedItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            plannedItemIds.Add(item.Item.Id);

        var blockedItemIds = new HashSet<string>(StringComparer.Ordinal);
        _log($"Sync push upload category started. Kind={SyncObjectKind.Item}; Count={items.Count}.");

        foreach (var item in items)
        {
            if (HasBlockedParent(item.Item, plannedItemIds, blockedItemIds))
            {
                blockedItemIds.Add(item.Item.Id);
                _log("Sync push object skipped. Kind=Item; Reason=blocked-parent.");
                continue;
            }

            if (HasBlockedDependency(
                    item.Item,
                    blockedRegularIconAssetIds,
                    blockedSecretIconAssetIds,
                    blockedCryptoProfileIds))
            {
                blockedItemIds.Add(item.Item.Id);
                _log("Sync push object skipped. Kind=Item; Reason=blocked-dependency.");
                continue;
            }

            var dto = _dtoMapper.ToDto(
                item,
                secretGenerationByProfileId,
                iconAssetsById,
                secretIconAssetsById);
            var uploaded = await UploadObjectAsync(
                    new SyncObjectIdentity(SyncObjectKind.Item, item.Item.Id),
                    dto,
                    item.SyncMetadata.RemoteEtag,
                    dto.ContentHash,
                    syncedAtUtc,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!uploaded)
                blockedItemIds.Add(item.Item.Id);
        }
        _log(
            "Sync push upload category finished. " +
            $"Kind={SyncObjectKind.Item}; " +
            $"BlockedItems={blockedItemIds.Count}.");
    }

    private async Task<bool> UploadObjectAsync<T>(
        SyncObjectIdentity identity,
        T dto,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc,
        PushResult result,
        CancellationToken cancellationToken)
    {
        var bytes = _serializer.Serialize(dto);
        _log(
            "Sync push object upload started. " +
            $"Kind={identity.Kind}; " +
            $"Mode={(remoteEtag is null ? "Create" : "Update")}; " +
            $"Bytes={bytes.Length}.");
        var putResult = await _transport
            .PutAsync(
                SyncRemoteObjectPath.ToRelativePath(identity),
                bytes,
                remoteEtag,
                createOnly: remoteEtag is null,
                cancellationToken)
            .ConfigureAwait(false);

        if (putResult.Status == SyncPutStatus.CreatedOrUpdated)
        {
            _localStore.MarkUploaded(identity, putResult.ETag, contentHash, syncedAtUtc);
            result.UploadedCount++;
            _log(
                "Sync push object upload succeeded. " +
                $"Kind={identity.Kind}; " +
                $"RemoteEtagPresent={putResult.ETag is not null}.");
            return true;
        }

        _localStore.MarkConflict(identity, PreconditionFailedReasonCode);
        result.ConflictCount++;
        _log($"Sync push object upload conflicted. Kind={identity.Kind}; Status={putResult.Status}.");
        return false;
    }

    private static bool HasBlockedDependency(
        BookmarkItemRecord item,
        HashSet<string> blockedRegularIconAssetIds,
        HashSet<string> blockedSecretIconAssetIds,
        HashSet<long> blockedCryptoProfileIds)
    {
        if (item.IconAssetId is not null && blockedRegularIconAssetIds.Contains(item.IconAssetId))
            return true;

        if (item.SecretIconAssetId is not null && blockedSecretIconAssetIds.Contains(item.SecretIconAssetId))
            return true;

        var cryptoProfileId = item.EncryptedPayload?.CryptoProfileId;
        return cryptoProfileId is not null && blockedCryptoProfileIds.Contains(cryptoProfileId.Value);
    }

    private static bool HasBlockedParent(
        BookmarkItemRecord item,
        HashSet<string> plannedItemIds,
        HashSet<string> blockedItemIds)
    {
        return item.ParentId is not null &&
            plannedItemIds.Contains(item.ParentId) &&
            blockedItemIds.Contains(item.ParentId);
    }

    private sealed class PushResult
    {
        public int UploadedCount { get; set; }

        public int ConflictCount { get; set; }

        public int ErrorCount { get; set; }
    }
}
