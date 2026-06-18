using System;

namespace Stranichnik.Sync;

public interface ISyncOperationGate
{
    bool CanApplySync { get; }

    IDisposable EnterLocalWriteOperation();

    IDisposable EnterEditorSession();
}
