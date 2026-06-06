using System;

namespace Stranichnik.Security;

public sealed class SecretSessionService : ISecretSessionService
{
    private RuntimeSecretKey? _dataKey;
    private bool _isDisposed;

    public SecretSessionStatus Status { get; private set; } = SecretSessionStatus.NotConfigured;

    public bool IsConfigured => Status != SecretSessionStatus.NotConfigured;

    public bool IsUnlocked =>
        Status is SecretSessionStatus.ConfiguredUnlockedHidden or
            SecretSessionStatus.ConfiguredUnlockedVisible;

    public bool AreSecretsVisible => Status == SecretSessionStatus.ConfiguredUnlockedVisible;

    public event EventHandler<SecretSessionChangedEventArgs>? StateChanged;

    public void MarkNotConfigured()
    {
        ThrowIfDisposed();
        DisposeDataKey();
        SetStatus(SecretSessionStatus.NotConfigured);
    }

    public void MarkConfiguredLocked()
    {
        ThrowIfDisposed();
        DisposeDataKey();
        SetStatus(SecretSessionStatus.ConfiguredLocked);
    }

    public void ConfigureAndUnlock(RuntimeSecretKey dataKey, bool showSecrets)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dataKey);
        ReplaceDataKey(dataKey);
        SetStatus(ToUnlockedStatus(showSecrets));
    }

    public void Unlock(RuntimeSecretKey dataKey, bool showSecrets)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dataKey);

        if (Status == SecretSessionStatus.NotConfigured)
            throw new InvalidOperationException("Secret session is not configured.");

        ReplaceDataKey(dataKey);
        SetStatus(ToUnlockedStatus(showSecrets));
    }

    public void ShowSecrets()
    {
        ThrowIfDisposed();
        EnsureUnlocked();
        SetStatus(SecretSessionStatus.ConfiguredUnlockedVisible);
    }

    public void HideSecrets()
    {
        ThrowIfDisposed();

        if (Status == SecretSessionStatus.ConfiguredUnlockedVisible)
            SetStatus(SecretSessionStatus.ConfiguredUnlockedHidden);
    }

    public void LockAndForgetKey()
    {
        ThrowIfDisposed();

        if (Status == SecretSessionStatus.NotConfigured)
            return;

        DisposeDataKey();
        SetStatus(SecretSessionStatus.ConfiguredLocked);
    }

    public RuntimeSecretKey? BorrowDataKey()
    {
        ThrowIfDisposed();
        return _dataKey;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        DisposeDataKey();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static SecretSessionStatus ToUnlockedStatus(bool showSecrets)
    {
        return showSecrets
            ? SecretSessionStatus.ConfiguredUnlockedVisible
            : SecretSessionStatus.ConfiguredUnlockedHidden;
    }

    private void ReplaceDataKey(RuntimeSecretKey dataKey)
    {
        if (ReferenceEquals(_dataKey, dataKey))
            return;

        DisposeDataKey();
        _dataKey = dataKey;
    }

    private void DisposeDataKey()
    {
        _dataKey?.Dispose();
        _dataKey = null;
    }

    private void EnsureUnlocked()
    {
        if (!IsUnlocked)
            throw new InvalidOperationException("Secret session is locked.");
    }

    private void SetStatus(SecretSessionStatus newStatus)
    {
        if (Status == newStatus)
            return;

        var oldStatus = Status;
        Status = newStatus;
        StateChanged?.Invoke(this, new SecretSessionChangedEventArgs(oldStatus, newStatus));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
