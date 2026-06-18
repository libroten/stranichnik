using Stranichnik.Sync;

namespace Stranichnik.Sync.Remote;

public sealed record SyncRemoteReadResult<T>(
    SyncRemoteReadStatus Status,
    SyncRemoteObjectInfo RemoteInfo,
    SyncObjectIdentity? Identity,
    T? Value);

public static class SyncRemoteReadResult
{
    public static SyncRemoteReadResult<T> Success<T>(
        SyncRemoteObjectInfo remoteInfo,
        SyncObjectIdentity identity,
        T value)
    {
        return new SyncRemoteReadResult<T>(
            SyncRemoteReadStatus.Success,
            remoteInfo,
            identity,
            value);
    }

    public static SyncRemoteReadResult<T> Failed<T>(
        SyncRemoteReadStatus status,
        SyncRemoteObjectInfo remoteInfo,
        SyncObjectIdentity? identity = null)
    {
        return new SyncRemoteReadResult<T>(
            status,
            remoteInfo,
            identity,
            default);
    }
}
