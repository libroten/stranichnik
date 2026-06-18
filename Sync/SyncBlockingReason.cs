namespace Stranichnik.Sync;

public enum SyncBlockingReason
{
    None,
    PendingAsset,
    PendingCryptoProfile,
    InvalidRemoteObject,
    LocalOperationActive,
    RemoteUnavailable,
    WrongCredentials,
    UnsupportedRepositoryVersion,
    InvalidRepository
}
