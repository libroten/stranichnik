using System.Collections.Generic;
using Stranichnik.Sync.Local;

namespace Stranichnik.Sync.Pull;

public sealed record SyncPullPlan(
    SyncApplyBatch ApplyBatch,
    IReadOnlyList<SyncPullConflict> Conflicts,
    IReadOnlyList<SyncPullMatchedDirtyObject> MatchedDirtyObjects,
    IReadOnlyList<SyncPullSatisfiedResetEvent> SatisfiedResetEvents,
    IReadOnlyList<SyncPullQuarantineCandidate> QuarantinedRemoteObjects,
    IReadOnlyList<SyncPullQuarantineCandidate> KnownQuarantinedRemoteObjects,
    IReadOnlyList<string> ResolvedQuarantinedRemoteObjectIds,
    IReadOnlyList<SyncObjectIdentity> MissingRemoteObjects,
    SyncSecretConflictConfirmation? SecretConflictConfirmation = null);
