using System;

namespace Stranichnik.Security;

public sealed class SecretSessionChangedEventArgs : EventArgs
{
    public SecretSessionChangedEventArgs(
        SecretSessionStatus oldStatus,
        SecretSessionStatus newStatus)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public SecretSessionStatus OldStatus { get; }

    public SecretSessionStatus NewStatus { get; }
}
