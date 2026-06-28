using System;

namespace Stranichnik.Sync;

public sealed record SyncRunSummary(
    bool Succeeded,
    int DownloadedCount,
    int UploadedCount,
    int ConflictCount,
    int PendingAssetCount,
    int PendingCryptoProfileCount,
    int InvalidRemoteObjectCount,
    int ErrorCount,
    SyncBlockingReason BlockingReason,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    SyncSecretConflictConfirmationReason? SecretConflictConfirmationReason = null);
