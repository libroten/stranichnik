using Stranichnik.Security;

namespace Stranichnik.Sync.Local;

public sealed record SyncCryptoProfileSnapshotRecord(
    CryptoProfileRecord Profile,
    SyncObjectMetadata SyncMetadata);
