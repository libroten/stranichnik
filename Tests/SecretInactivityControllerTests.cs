using System;
using Stranichnik.Security;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretInactivityControllerTests
{
    [Fact]
    public void ResumeRestartsTimeoutAfterPause()
    {
        var timer = new FakeSecretInactivityTimer();
        var hideCalls = 0;
        var controller = new SecretInactivityController(timer, () => hideCalls++);

        controller.SetSecretsVisible(true);
        controller.Pause();
        controller.NotifyActivity();
        controller.Resume();

        Assert.Equal(2, timer.StartCount);
        Assert.Equal(0, hideCalls);
    }

    [Fact]
    public void ActivityRestartsTimeoutWhenSecretsAreVisible()
    {
        var timer = new FakeSecretInactivityTimer();
        var controller = new SecretInactivityController(timer, () => { });

        controller.SetSecretsVisible(true);
        controller.NotifyActivity();

        Assert.Equal(2, timer.StartCount);
    }

    [Fact]
    public void ActivityDoesNotStartTimeoutWhenSecretsAreHidden()
    {
        var timer = new FakeSecretInactivityTimer();
        var controller = new SecretInactivityController(timer, () => { });

        controller.SetSecretsVisible(false);
        controller.NotifyActivity();

        Assert.Equal(0, timer.StartCount);
    }

    [Fact]
    public void NestedPauseRestartsTimeoutOnlyAfterFinalResume()
    {
        var timer = new FakeSecretInactivityTimer();
        var controller = new SecretInactivityController(timer, () => { });

        controller.SetSecretsVisible(true);
        controller.Pause();
        controller.Pause();
        controller.Resume();
        controller.Resume();

        Assert.Equal(2, timer.StartCount);
    }

    [Fact]
    public void ResumeDoesNotRestartTimeoutIfSecretsWereHiddenWhilePaused()
    {
        var timer = new FakeSecretInactivityTimer();
        var controller = new SecretInactivityController(timer, () => { });

        controller.SetSecretsVisible(true);
        controller.Pause();
        controller.SetSecretsVisible(false);
        controller.Resume();

        Assert.Equal(1, timer.StartCount);
    }

    [Fact]
    public void TimerTickHidesSecretsOnce()
    {
        var timer = new FakeSecretInactivityTimer();
        var hideCalls = 0;
        var controller = new SecretInactivityController(timer, () => hideCalls++);

        controller.SetSecretsVisible(true);
        timer.FireTick();
        timer.FireTick();

        Assert.Equal(1, hideCalls);
    }

    private sealed class FakeSecretInactivityTimer : ISecretInactivityTimer
    {
        public event EventHandler? Tick;

        public int StartCount { get; private set; }

        public void StartTimer()
        {
            StartCount++;
        }

        public void StopTimer()
        {
        }

        public void FireTick()
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
