using System;

namespace Stranichnik.Sync.Local;

public static class SyncQuarantinedRemoteObjectId
{
    public static string FromRemotePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return relativePath;
    }

    public static string FromIdentity(SyncObjectIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Id);

        return $"{identity.Kind}:{identity.Id}";
    }
}
