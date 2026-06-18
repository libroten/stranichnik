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
        Action<string>? log = null)
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
            _log("Sync run started.");
            var initializationResult = await _repositoryInitializer
                .EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            if (initializationResult.Status != SyncRepositoryInitializationStatus.Ready)
            {
                var blockingReason = ToBlockingReason(initializationResult.Status);
                _log($"Sync run stopped: repository initialization status={initializationResult.Status}.");
                return CreateBlockedSummary(startedAtUtc, blockingReason);
            }

            var pullSummary = await _pullService.PullAsync(cancellationToken).ConfigureAwait(false);
            var pushSummary = await _pushService.PushAsync(cancellationToken).ConfigureAwait(false);
            var finishedAtUtc = _clock();
            var summary = MergeSummaries(startedAtUtc, finishedAtUtc, pullSummary, pushSummary);

            LogSyncFinished(summary);
            return summary;
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
            FinishedAtUtc: finishedAtUtc);
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
