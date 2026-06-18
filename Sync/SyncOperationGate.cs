using System;

namespace Stranichnik.Sync;

public sealed class SyncOperationGate : ISyncOperationGate
{
    private readonly object _syncRoot = new();
    private int _activeOperationCount;

    public bool CanApplySync
    {
        get
        {
            lock (_syncRoot)
                return _activeOperationCount == 0;
        }
    }

    public IDisposable EnterLocalWriteOperation()
    {
        return EnterOperation();
    }

    public IDisposable EnterEditorSession()
    {
        return EnterOperation();
    }

    private OperationScope EnterOperation()
    {
        lock (_syncRoot)
            _activeOperationCount++;

        return new OperationScope(this);
    }

    private void LeaveOperation()
    {
        lock (_syncRoot)
        {
            if (_activeOperationCount == 0)
                throw new InvalidOperationException("Sync operation gate scope was disposed too many times.");

            _activeOperationCount--;
        }
    }

    private sealed class OperationScope : IDisposable
    {
        private SyncOperationGate? _owner;

        public OperationScope(SyncOperationGate owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
                return;

            _owner = null;
            owner.LeaveOperation();
        }
    }
}
