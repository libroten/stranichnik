using System;

namespace Stranichnik.Security;

public interface ISecretSessionService : IDisposable
{
    SecretSessionStatus Status { get; }

    bool IsConfigured { get; }

    bool IsUnlocked { get; }

    bool AreSecretsVisible { get; }

    event EventHandler<SecretSessionChangedEventArgs>? StateChanged;

    void MarkNotConfigured();

    void MarkConfiguredLocked();

    void ConfigureAndUnlock(RuntimeSecretKey dataKey, bool showSecrets);

    void Unlock(RuntimeSecretKey dataKey, bool showSecrets);

    void ShowSecrets();

    void HideSecrets();

    void LockAndForgetKey();

    RuntimeSecretKey? BorrowDataKey();
}
