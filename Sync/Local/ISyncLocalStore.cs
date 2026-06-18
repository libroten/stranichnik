using System;
using Stranichnik.Sync;

namespace Stranichnik.Sync.Local;

public interface ISyncLocalStore
{
    SyncLocalSnapshot LoadSnapshot();

    void ApplyRemoteChanges(SyncApplyBatch batch);

    void MarkUploaded(
        SyncObjectIdentity identity,
        string? remoteEtag,
        string contentHash,
        DateTimeOffset syncedAtUtc);

    void MarkConflict(SyncObjectIdentity identity, string reasonCode);

    void MarkQuarantinedRemoteObject(
        string objectKind,
        string relativePath,
        string? remoteEtag,
        string? contentHash,
        string reasonCode,
        DateTimeOffset seenAtUtc);
}
