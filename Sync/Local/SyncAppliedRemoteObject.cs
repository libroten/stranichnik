using Stranichnik.Sync;
using Stranichnik.Sync.Remote;

namespace Stranichnik.Sync.Local;

public sealed record SyncAppliedRemoteObject<T>(
    SyncRemoteObjectInfo RemoteInfo,
    SyncObjectIdentity Identity,
    T Value);
