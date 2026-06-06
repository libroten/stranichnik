using System;
using Stranichnik.Security;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretSessionServiceTests
{
    [Fact]
    public void New_session_starts_not_configured()
    {
        using var service = new SecretSessionService();

        Assert.Equal(SecretSessionStatus.NotConfigured, service.Status);
        Assert.False(service.IsConfigured);
        Assert.False(service.IsUnlocked);
        Assert.False(service.AreSecretsVisible);
        Assert.Null(service.BorrowDataKey());
    }

    [Fact]
    public void MarkConfiguredLocked_sets_locked_configured_state()
    {
        using var service = new SecretSessionService();

        service.MarkConfiguredLocked();

        Assert.Equal(SecretSessionStatus.ConfiguredLocked, service.Status);
        Assert.True(service.IsConfigured);
        Assert.False(service.IsUnlocked);
        Assert.False(service.AreSecretsVisible);
        Assert.Null(service.BorrowDataKey());
    }

    [Fact]
    public void ConfigureAndUnlock_stores_key_and_can_show_secrets()
    {
        using var service = new SecretSessionService();
        using var key = CreateKey();

        service.ConfigureAndUnlock(key, showSecrets: true);

        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedVisible, service.Status);
        Assert.True(service.IsConfigured);
        Assert.True(service.IsUnlocked);
        Assert.True(service.AreSecretsVisible);
        Assert.Same(key, service.BorrowDataKey());
        Assert.False(key.IsDisposed);
    }

    [Fact]
    public void Show_and_hide_do_not_dispose_key()
    {
        using var service = new SecretSessionService();
        using var key = CreateKey();
        service.ConfigureAndUnlock(key, showSecrets: false);

        service.ShowSecrets();
        service.HideSecrets();

        Assert.Equal(SecretSessionStatus.ConfiguredUnlockedHidden, service.Status);
        Assert.Same(key, service.BorrowDataKey());
        Assert.False(key.IsDisposed);
    }

    [Fact]
    public void LockAndForgetKey_disposes_key_and_keeps_configured_state()
    {
        using var service = new SecretSessionService();
        using var key = CreateKey();
        service.ConfigureAndUnlock(key, showSecrets: true);

        service.LockAndForgetKey();

        Assert.Equal(SecretSessionStatus.ConfiguredLocked, service.Status);
        Assert.Null(service.BorrowDataKey());
        Assert.True(key.IsDisposed);
    }

    [Fact]
    public void MarkNotConfigured_disposes_key_and_clears_configured_state()
    {
        using var service = new SecretSessionService();
        using var key = CreateKey();
        service.ConfigureAndUnlock(key, showSecrets: true);

        service.MarkNotConfigured();

        Assert.Equal(SecretSessionStatus.NotConfigured, service.Status);
        Assert.False(service.IsConfigured);
        Assert.Null(service.BorrowDataKey());
        Assert.True(key.IsDisposed);
    }

    [Fact]
    public void Unlock_requires_configured_session()
    {
        using var service = new SecretSessionService();
        using var key = CreateKey();

        Assert.Throws<InvalidOperationException>(() => service.Unlock(key, showSecrets: true));
    }

    [Fact]
    public void ShowSecrets_requires_unlocked_session()
    {
        using var service = new SecretSessionService();
        service.MarkConfiguredLocked();

        Assert.Throws<InvalidOperationException>(service.ShowSecrets);
    }

    [Fact]
    public void StateChanged_fires_only_when_status_changes()
    {
        using var service = new SecretSessionService();
        var eventCount = 0;
        SecretSessionChangedEventArgs? lastEvent = null;
        service.StateChanged += (_, args) =>
        {
            eventCount++;
            lastEvent = args;
        };

        service.MarkConfiguredLocked();
        service.MarkConfiguredLocked();

        Assert.Equal(1, eventCount);
        Assert.Equal(SecretSessionStatus.NotConfigured, lastEvent?.OldStatus);
        Assert.Equal(SecretSessionStatus.ConfiguredLocked, lastEvent?.NewStatus);
    }

    [Fact]
    public void Dispose_disposes_owned_key()
    {
        var service = new SecretSessionService();
        using var key = CreateKey();
        service.ConfigureAndUnlock(key, showSecrets: true);

        service.Dispose();

        Assert.True(key.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => service.BorrowDataKey());
    }

    private static RuntimeSecretKey CreateKey()
    {
        return new RuntimeSecretKey(new byte[SecretEncryptionConstants.DataKeyLengthBytes]);
    }
}
