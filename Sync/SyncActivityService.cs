using System;

namespace Stranichnik.Sync;

public sealed class SyncActivityService : ISyncActivityService
{
    private readonly object _syncRoot = new();
    private int _activeOperationCount;

    public event EventHandler<SyncActivityChangedEventArgs>? ActivityChanged;

    public event EventHandler<SyncActivityCompletedEventArgs>? ActivityCompleted;

    public bool IsActive
    {
        get
        {
            lock (_syncRoot)
                return _activeOperationCount > 0;
        }
    }

    public IDisposable BeginOperation()
    {
        var shouldNotify = false;
        lock (_syncRoot)
        {
            shouldNotify = _activeOperationCount == 0;
            _activeOperationCount++;
        }

        if (shouldNotify)
            ActivityChanged?.Invoke(this, new SyncActivityChangedEventArgs(isActive: true));

        return new ActivityScope(this);
    }

    public void ReportCompleted(bool succeeded)
    {
        ActivityCompleted?.Invoke(this, new SyncActivityCompletedEventArgs(succeeded));
    }

    private void EndOperation()
    {
        var shouldNotify = false;
        lock (_syncRoot)
        {
            if (_activeOperationCount == 0)
                throw new InvalidOperationException("Sync activity scope was disposed too many times.");

            _activeOperationCount--;
            shouldNotify = _activeOperationCount == 0;
        }

        if (shouldNotify)
            ActivityChanged?.Invoke(this, new SyncActivityChangedEventArgs(isActive: false));
    }

    private sealed class ActivityScope : IDisposable
    {
        private SyncActivityService? _owner;

        public ActivityScope(SyncActivityService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
                return;

            _owner = null;
            owner.EndOperation();
        }
    }
}
