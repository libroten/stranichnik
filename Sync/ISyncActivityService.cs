using System;

namespace Stranichnik.Sync;

public interface ISyncActivityService
{
    event EventHandler<SyncActivityChangedEventArgs>? ActivityChanged;

    bool IsActive { get; }

    IDisposable BeginOperation();
}
