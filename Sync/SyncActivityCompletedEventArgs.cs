using System;

namespace Stranichnik.Sync;

public sealed class SyncActivityCompletedEventArgs : EventArgs
{
    public SyncActivityCompletedEventArgs(bool succeeded)
    {
        Succeeded = succeeded;
    }

    public bool Succeeded { get; }
}
