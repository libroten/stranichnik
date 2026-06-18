using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;

namespace Stranichnik.Sync.Pull;

public sealed class SyncPullService
{
    private readonly ISyncLocalStore _localStore;
    private readonly SyncRemoteObjectReader _remoteReader;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;

    public SyncPullService(
        ISyncLocalStore localStore,
        SyncRemoteObjectReader remoteReader,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(localStore);
        ArgumentNullException.ThrowIfNull(remoteReader);

        _localStore = localStore;
        _remoteReader = remoteReader;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? Logs.Print;
    }

    public async Task<SyncRunSummary> PullAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock();
        _log("Sync pull started.");

        var snapshot = _localStore.LoadSnapshot();
        var secretResetEvents = await _remoteReader
            .ReadObjectsAsync<SyncSecretResetEventDto>(SyncObjectKind.SecretResetEvent, cancellationToken)
            .ConfigureAwait(false);
        var cryptoProfiles = await _remoteReader
            .ReadObjectsAsync<SyncCryptoProfileDto>(SyncObjectKind.CryptoProfile, cancellationToken)
            .ConfigureAwait(false);
        var iconAssets = await _remoteReader
            .ReadObjectsAsync<SyncIconAssetDto>(SyncObjectKind.IconAsset, cancellationToken)
            .ConfigureAwait(false);
        var secretIconAssets = await _remoteReader
            .ReadObjectsAsync<SyncSecretIconAssetDto>(SyncObjectKind.SecretIconAsset, cancellationToken)
            .ConfigureAwait(false);
        var items = await _remoteReader
            .ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, cancellationToken)
            .ConfigureAwait(false);

        var plan = SyncPullPlanner.Plan(
            snapshot,
            secretResetEvents,
            cryptoProfiles,
            iconAssets,
            secretIconAssets,
            items);
        var syncedAtUtc = _clock();

        _localStore.ApplyRemoteChanges(plan.ApplyBatch);

        foreach (var matchedObject in plan.MatchedDirtyObjects)
        {
            _localStore.MarkUploaded(
                matchedObject.Identity,
                matchedObject.RemoteEtag,
                matchedObject.ContentHash,
                syncedAtUtc);
        }

        foreach (var conflict in plan.Conflicts)
            _localStore.MarkConflict(conflict.Identity, conflict.ReasonCode);

        foreach (var quarantineCandidate in plan.QuarantinedRemoteObjects)
        {
            _localStore.MarkQuarantinedRemoteObject(
                quarantineCandidate.ObjectKind,
                quarantineCandidate.RelativePath,
                quarantineCandidate.RemoteEtag,
                quarantineCandidate.ContentHash,
                quarantineCandidate.ReasonCode,
                syncedAtUtc);
        }

        var finishedSnapshot = _localStore.LoadSnapshot();
        var finishedAtUtc = _clock();
        var summary = new SyncRunSummary(
            Succeeded: plan.Conflicts.Count == 0 && plan.QuarantinedRemoteObjects.Count == 0,
            DownloadedCount:
                CountSuccessful(secretResetEvents) +
                CountSuccessful(cryptoProfiles) +
                CountSuccessful(iconAssets) +
                CountSuccessful(secretIconAssets) +
                CountSuccessful(items),
            UploadedCount: 0,
            ConflictCount: plan.Conflicts.Count,
            PendingAssetCount: finishedSnapshot.PendingAssetRefs.Count,
            PendingCryptoProfileCount: finishedSnapshot.DeferredSecretItems.Count,
            InvalidRemoteObjectCount: plan.QuarantinedRemoteObjects.Count,
            ErrorCount: 0,
            BlockingReason: SyncBlockingReason.None,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: finishedAtUtc);

        _log(
            "Sync pull finished. " +
            $"Succeeded={summary.Succeeded}; " +
            $"Downloaded={summary.DownloadedCount}; " +
            $"Conflicts={summary.ConflictCount}; " +
            $"PendingAssets={summary.PendingAssetCount}; " +
            $"PendingCryptoProfiles={summary.PendingCryptoProfileCount}; " +
            $"InvalidRemoteObjects={summary.InvalidRemoteObjectCount}; " +
            $"Errors={summary.ErrorCount}.");

        return summary;
    }

    private static int CountSuccessful<T>(
        System.Collections.Generic.IReadOnlyList<SyncRemoteReadResult<T>> results)
    {
        return results.Count(result => result.Status == SyncRemoteReadStatus.Success);
    }
}
