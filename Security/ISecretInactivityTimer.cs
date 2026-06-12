using System;

namespace Stranichnik.Security;

public interface ISecretInactivityTimer
{
    event EventHandler? Tick;

    void StartTimer();

    void StopTimer();
}
