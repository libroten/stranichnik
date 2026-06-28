using System;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
using Stranichnik.Sync.Pull;
using Stranichnik.Sync.Push;

namespace Stranichnik.Sync;

public sealed class SyncApplicationService : IDisposable
{
    private readonly SyncRepositoryInitializer _repositoryInitializer;
    private readonly SyncPullService _pullService;
    private readonly SyncPushService _pushService;
    private readonly ISyncOperationGate _operationGate;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;
    private readonly ISyncActivityService? _syncActivityService;
    private readonly IDisposable? _ownedResource;
    private readonly object _syncRunGate = new();
    private bool _isSyncRunning;

    public SyncApplicationService(
        SyncRepositoryInitializer repositoryInitializer,
        SyncPullService pullService,
        SyncPushService pushService,
        ISyncOperationGate operationGate,
        IDisposable? ownedResource = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        ISyncActivityService? syncActivityService = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryInitializer);
        ArgumentNullException.ThrowIfNull(pullService);
        ArgumentNullException.ThrowIfNull(pushService);
        ArgumentNullException.ThrowIfNull(operationGate);

        _repositoryInitializer = repositoryInitializer;
        _pullService = pullService;
        _pushService = pushService;
        _operationGate = operationGate;
        _ownedResource = ownedResource;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? Logs.Print;
        _syncActivityService = syncActivityService;
    }

    public async Task<SyncRunSummary> SyncNowAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock();
        if (!_operationGate.CanApplySync)
        {
            _log("Sync postponed: local operation is active.");
            return CreateBlockedSummary(startedAtUtc, SyncBlockingReason.LocalOperationActive);
        }

        if (!TryEnterSyncRun())
        {
            _log("Sync postponed: another sync run is active.");
            return CreateBlockedSummary(startedAtUtc, SyncBlockingReason.LocalOperationActive);
        }

        try
        {
            using var syncActivity = _syncActivityService?.BeginOperation();
            _log("Sync run started.");
            _log("Sync repository initialization started.");
            var initializationResult = await _repositoryInitializer
                .EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            if (initializationResult.Status != SyncRepositoryInitializationStatus.Ready)
            {
                var blockingReason = ToBlockingReason(initializationResult.Status);
                _log($"Sync run stopped: repository initialization status={initializationResult.Status}.");
                var blockedSummary = CreateBlockedSummary(startedAtUtc, blockingReason);
                _syncActivityService?.ReportCompleted(IsFullySuccessful(blockedSummary));
                return blockedSummary;
            }

            _log("Sync repository initialization finished: ready.");
            _log("Sync pull phase requested.");
            var pullSummary = await _pullService.PullAsync(cancellationToken).ConfigureAwait(false);
            _log(
                "Sync pull phase completed. " +
                $"Succeeded={pullSummary.Succeeded}; " +
                $"Downloaded={pullSummary.DownloadedCount}; " +
                $"Conflicts={pullSummary.ConflictCount}; " +
                $"InvalidRemoteObjects={pullSummary.InvalidRemoteObjectCount}; " +
                $"BlockingReason={pullSummary.BlockingReason}.");
            if (pullSummary.BlockingReason != SyncBlockingReason.None)
            {
                var blockedFinishedAtUtc = _clock();
                var blockedSummary = MergeSummaries(
                    startedAtUtc,
                    blockedFinishedAtUtc,
                    pullSummary,
                    CreateEmptySummary(blockedFinishedAtUtc));
                LogSyncFinished(blockedSummary);
                _syncActivityService?.ReportCompleted(IsFullySuccessful(blockedSummary));
                return blockedSummary;
            }

            _log("Sync push phase requested.");
            var pushSummary = await _pushService.PushAsync(cancellationToken).ConfigureAwait(false);
            _log(
                "Sync push phase completed. " +
                $"Succeeded={pushSummary.Succeeded}; " +
                $"Uploaded={pushSummary.UploadedCount}; " +
                $"Conflicts={pushSummary.ConflictCount}; " +
                $"Errors={pushSummary.ErrorCount}.");
            var finishedAtUtc = _clock();
            var summary = MergeSummaries(startedAtUtc, finishedAtUtc, pullSummary, pushSummary);

            LogSyncFinished(summary);
            _syncActivityService?.ReportCompleted(IsFullySuccessful(summary));
            return summary;
        }
        catch
        {
            _syncActivityService?.ReportCompleted(succeeded: false);
            throw;
        }
        finally
        {
            LeaveSyncRun();
        }
    }

    private void LogSyncFinished(SyncRunSummary summary)
    {
        _log(
            "Sync run finished. " +
            $"Succeeded={summary.Succeeded}; " +
            $"Downloaded={summary.DownloadedCount}; " +
            $"Uploaded={summary.UploadedCount}; " +
            $"Conflicts={summary.ConflictCount}; " +
            $"PendingAssets={summary.PendingAssetCount}; " +
            $"PendingCryptoProfiles={summary.PendingCryptoProfileCount}; " +
            $"InvalidRemoteObjects={summary.InvalidRemoteObjectCount}; " +
            $"Errors={summary.ErrorCount}; " +
            $"BlockingReason={summary.BlockingReason}.");
    }

    private bool TryEnterSyncRun()
    {
        lock (_syncRunGate)
        {
            if (_isSyncRunning)
                return false;

            _isSyncRunning = true;
            return true;
        }
    }

    private void LeaveSyncRun()
    {
        lock (_syncRunGate)
            _isSyncRunning = false;
    }

    public void Dispose()
    {
        _ownedResource?.Dispose();
    }

    private SyncRunSummary CreateBlockedSummary(
        DateTimeOffset startedAtUtc,
        SyncBlockingReason blockingReason)
    {
        return new SyncRunSummary(
            Succeeded: false,
            DownloadedCount: 0,
            UploadedCount: 0,
            ConflictCount: 0,
            PendingAssetCount: 0,
            PendingCryptoProfileCount: 0,
            InvalidRemoteObjectCount: 0,
            ErrorCount: 0,
            BlockingReason: blockingReason,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: _clock());
    }

    private static SyncRunSummary MergeSummaries(
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        SyncRunSummary pullSummary,
        SyncRunSummary pushSummary)
    {
        return new SyncRunSummary(
            Succeeded: pullSummary.Succeeded && pushSummary.Succeeded,
            DownloadedCount: pullSummary.DownloadedCount + pushSummary.DownloadedCount,
            UploadedCount: pullSummary.UploadedCount + pushSummary.UploadedCount,
            ConflictCount: pullSummary.ConflictCount + pushSummary.ConflictCount,
            PendingAssetCount: Math.Max(pullSummary.PendingAssetCount, pushSummary.PendingAssetCount),
            PendingCryptoProfileCount: Math.Max(pullSummary.PendingCryptoProfileCount, pushSummary.PendingCryptoProfileCount),
            InvalidRemoteObjectCount: Math.Max(
                pullSummary.InvalidRemoteObjectCount,
                pushSummary.InvalidRemoteObjectCount),
            ErrorCount: pullSummary.ErrorCount + pushSummary.ErrorCount,
            BlockingReason: pullSummary.BlockingReason != SyncBlockingReason.None
                ? pullSummary.BlockingReason
                : pushSummary.BlockingReason,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: finishedAtUtc,
            SecretConflictConfirmationReason: pullSummary.SecretConflictConfirmationReason ??
                pushSummary.SecretConflictConfirmationReason);
    }

    private static SyncRunSummary CreateEmptySummary(DateTimeOffset timestamp)
    {
        return new SyncRunSummary(
            Succeeded: true,
            DownloadedCount: 0,
            UploadedCount: 0,
            ConflictCount: 0,
            PendingAssetCount: 0,
            PendingCryptoProfileCount: 0,
            InvalidRemoteObjectCount: 0,
            ErrorCount: 0,
            BlockingReason: SyncBlockingReason.None,
            StartedAtUtc: timestamp,
            FinishedAtUtc: timestamp);
    }

    private static bool IsFullySuccessful(SyncRunSummary summary)
    {
        return summary.Succeeded &&
            summary.ConflictCount == 0 &&
            summary.PendingAssetCount == 0 &&
            summary.PendingCryptoProfileCount == 0 &&
            summary.InvalidRemoteObjectCount == 0 &&
            summary.ErrorCount == 0 &&
            summary.BlockingReason == SyncBlockingReason.None;
    }

    private static SyncBlockingReason ToBlockingReason(SyncRepositoryInitializationStatus status)
    {
        return status switch
        {
            SyncRepositoryInitializationStatus.UnsupportedRepositoryVersion => SyncBlockingReason.UnsupportedRepositoryVersion,
            SyncRepositoryInitializationStatus.InvalidRepository => SyncBlockingReason.InvalidRepository,
            SyncRepositoryInitializationStatus.Ready => SyncBlockingReason.None,
            _ => SyncBlockingReason.InvalidRepository
        };
    }
}
