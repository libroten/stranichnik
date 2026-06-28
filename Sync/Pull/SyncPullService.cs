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
        _log(
            "Sync pull local snapshot loaded. " +
            $"Items={snapshot.Items.Count}; " +
            $"IconAssets={snapshot.IconAssets.Count}; " +
            $"SecretIconAssets={snapshot.SecretIconAssets.Count}; " +
            $"CryptoProfiles={snapshot.CryptoProfiles.Count}; " +
            $"ResetEvents={snapshot.SecretResetEvents.Count}; " +
            $"PendingAssets={snapshot.PendingAssetRefs.Count}; " +
            $"DeferredSecretItems={snapshot.DeferredSecretItems.Count}; " +
            $"QuarantinedRemoteObjects={snapshot.QuarantinedRemoteObjects.Count}.");
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
        var remoteReadSuccessCount =
            CountSuccessful(secretResetEvents) +
            CountSuccessful(cryptoProfiles) +
            CountSuccessful(iconAssets) +
            CountSuccessful(secretIconAssets) +
            CountSuccessful(items);

        _log(
            "Sync pull plan created. " +
            $"RemoteReadSuccess={remoteReadSuccessCount}; " +
            $"ApplyResetEvents={plan.ApplyBatch.SecretResetEvents.Count}; " +
            $"ApplyCryptoProfiles={plan.ApplyBatch.CryptoProfiles.Count}; " +
            $"ApplyIconAssets={plan.ApplyBatch.IconAssets.Count}; " +
            $"ApplySecretIconAssets={plan.ApplyBatch.SecretIconAssets.Count}; " +
            $"ApplyItems={plan.ApplyBatch.Items.Count}; " +
            $"MatchedDirty={plan.MatchedDirtyObjects.Count}; " +
            $"Conflicts={plan.Conflicts.Count}; " +
            $"FreshInvalidRemoteObjects={plan.QuarantinedRemoteObjects.Count}; " +
            $"KnownInvalidRemoteObjects={plan.KnownQuarantinedRemoteObjects.Count}; " +
            $"ResolvedInvalidRemoteObjects={plan.ResolvedQuarantinedRemoteObjectIds.Count}; " +
            $"MissingRemoteObjects={plan.MissingRemoteObjects.Count}.");

        if (plan.SecretConflictConfirmation is not null)
        {
            var confirmationFinishedAtUtc = _clock();
            var confirmationInvalidRemoteObjectCount =
                plan.QuarantinedRemoteObjects.Count + plan.KnownQuarantinedRemoteObjects.Count;
            var confirmationSummary = new SyncRunSummary(
                Succeeded: false,
                DownloadedCount: 0,
                UploadedCount: 0,
                ConflictCount: plan.Conflicts.Count,
                PendingAssetCount: snapshot.PendingAssetRefs.Count,
                PendingCryptoProfileCount: snapshot.DeferredSecretItems.Count,
                InvalidRemoteObjectCount: confirmationInvalidRemoteObjectCount,
                ErrorCount: 0,
                BlockingReason: SyncBlockingReason.NeedsSecretConflictConfirmation,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: confirmationFinishedAtUtc,
                SecretConflictConfirmationReason: plan.SecretConflictConfirmation.Reason);
            _log(
                "Sync pull stopped: secret conflict confirmation required. " +
                $"Reason={plan.SecretConflictConfirmation.Reason}; " +
                $"Conflicts={confirmationSummary.ConflictCount}; " +
                $"InvalidRemoteObjects={confirmationSummary.InvalidRemoteObjectCount}.");
            return confirmationSummary;
        }

        _log("Sync pull local apply started.");
        _localStore.ApplyPullPlan(plan, syncedAtUtc);
        _log("Sync pull local apply finished.");

        var finishedSnapshot = _localStore.LoadSnapshot();
        _log(
            "Sync pull finished snapshot loaded. " +
            $"PendingAssets={finishedSnapshot.PendingAssetRefs.Count}; " +
            $"DeferredSecretItems={finishedSnapshot.DeferredSecretItems.Count}; " +
            $"QuarantinedRemoteObjects={finishedSnapshot.QuarantinedRemoteObjects.Count}.");
        var finishedAtUtc = _clock();
        var invalidRemoteObjectCount = Math.Max(
            finishedSnapshot.QuarantinedRemoteObjects.Count,
            plan.QuarantinedRemoteObjects.Count + plan.KnownQuarantinedRemoteObjects.Count);
        var summary = new SyncRunSummary(
            Succeeded: plan.Conflicts.Count == 0 && plan.QuarantinedRemoteObjects.Count == 0,
            DownloadedCount: CountAppliedRemoteChanges(plan.ApplyBatch),
            UploadedCount: 0,
            ConflictCount: plan.Conflicts.Count,
            PendingAssetCount: finishedSnapshot.PendingAssetRefs.Count,
            PendingCryptoProfileCount: finishedSnapshot.DeferredSecretItems.Count,
            InvalidRemoteObjectCount: invalidRemoteObjectCount,
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
            $"FreshInvalidRemoteObjects={plan.QuarantinedRemoteObjects.Count}; " +
            $"KnownInvalidRemoteObjects={plan.KnownQuarantinedRemoteObjects.Count}; " +
            $"ResolvedInvalidRemoteObjects={plan.ResolvedQuarantinedRemoteObjectIds.Count}; " +
            $"MissingRemoteObjects={plan.MissingRemoteObjects.Count}; " +
            $"Errors={summary.ErrorCount}.");

        return summary;
    }

    private static int CountAppliedRemoteChanges(SyncApplyBatch batch)
    {
        return batch.SecretResetEvents.Count +
            batch.CryptoProfiles.Count +
            batch.IconAssets.Count +
            batch.SecretIconAssets.Count +
            batch.Items.Count;
    }

    private static int CountSuccessful<T>(
        System.Collections.Generic.IReadOnlyList<SyncRemoteReadResult<T>> results)
    {
        return results.Count(result => result.Status == SyncRemoteReadStatus.Success);
    }
}
