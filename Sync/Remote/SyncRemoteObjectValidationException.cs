using System;

namespace Stranichnik.Sync.Remote;

public sealed class SyncRemoteObjectValidationException : Exception
{
    public SyncRemoteObjectValidationException(string message)
        : base(message)
    {
    }

    public SyncRemoteObjectValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
