using System;
using Stranichnik.Sync;
using Stranichnik.Sync.Pull;

namespace Stranichnik.Sync.Local;

public interface ISyncLocalStore
{
    SyncLocalSnapshot LoadSnapshot();

    void ApplyRemoteChanges(SyncApplyBatch batch);

    void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc);

    void MarkUploaded(
        SyncObjectIdentity identity,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc);

    void MarkConflict(SyncObjectIdentity identity, string reasonCode);

    void MarkDirty(SyncObjectIdentity identity);

    void MarkQuarantinedRemoteObject(
        string objectKind,
        string relativePath,
        string? remoteEtag,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc);

    void ClearQuarantinedRemoteObject(string id);
}
