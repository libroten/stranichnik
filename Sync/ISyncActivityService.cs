using System;

namespace Stranichnik.Sync;

public interface ISyncActivityService
{
    event EventHandler<SyncActivityChangedEventArgs>? ActivityChanged;

    event EventHandler<SyncActivityCompletedEventArgs>? ActivityCompleted;

    bool IsActive { get; }

    IDisposable BeginOperation();

    void ReportCompleted(bool succeeded);
}
