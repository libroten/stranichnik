using Stranichnik.Sync;

namespace Stranichnik.Sync.Pull;

public sealed record SyncSecretConflictConfirmation(
    SyncSecretConflictConfirmationReason Reason);
