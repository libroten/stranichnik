using System;

namespace Stranichnik.Sync;

public sealed class SyncActivityChangedEventArgs : EventArgs
{
    public SyncActivityChangedEventArgs(bool isActive)
    {
        IsActive = isActive;
    }

    public bool IsActive { get; }
}
