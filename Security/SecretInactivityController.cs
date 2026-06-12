using System;

namespace Stranichnik.Security;

public sealed class SecretInactivityController
{
    private readonly ISecretInactivityTimer _timer;
    private readonly Action _hideSecrets;
    private bool _areSecretsVisible;
    private int _pauseDepth;

    public SecretInactivityController(ISecretInactivityTimer timer, Action hideSecrets)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _hideSecrets = hideSecrets ?? throw new ArgumentNullException(nameof(hideSecrets));
        _timer.Tick += OnTimerTick;
    }

    public void SetSecretsVisible(bool areSecretsVisible)
    {
        _areSecretsVisible = areSecretsVisible;

        if (_areSecretsVisible && !IsPaused)
        {
            RestartTimer();
            return;
        }

        _timer.StopTimer();
    }

    public void NotifyActivity()
    {
        if (!_areSecretsVisible || IsPaused)
            return;

        RestartTimer();
    }

    public void Pause()
    {
        _pauseDepth++;
        _timer.StopTimer();
    }

    public void Resume()
    {
        if (_pauseDepth > 0)
            _pauseDepth--;

        if (_areSecretsVisible && !IsPaused)
            RestartTimer();
    }

    private bool IsPaused => _pauseDepth > 0;

    private void RestartTimer()
    {
        _timer.StopTimer();
        _timer.StartTimer();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.StopTimer();

        if (!_areSecretsVisible)
            return;

        _areSecretsVisible = false;
        _hideSecrets();
    }
}
