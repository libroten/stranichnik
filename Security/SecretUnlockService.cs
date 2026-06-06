using System;
using System.Diagnostics.CodeAnalysis;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretUnlockService
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The unlock service intentionally depends on the profile store abstraction so SQLite and tests can use different implementations.")]
    private readonly ISecretProfileStore _profileStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The unlock service intentionally depends on the crypto abstraction so unlock behavior can be tested independently.")]
    private readonly ISecretCryptoService _cryptoService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The unlock service intentionally depends on the secret session abstraction so session transitions can be tested independently.")]
    private readonly ISecretSessionService _secretSession;

    public SecretUnlockService(
        ISecretProfileStore profileStore,
        ISecretCryptoService cryptoService,
        ISecretSessionService secretSession)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(secretSession);

        _profileStore = profileStore;
        _cryptoService = cryptoService;
        _secretSession = secretSession;
    }

    public SecretSessionUnlockResult Unlock(string masterPassword, bool showSecrets)
    {
        var profile = _profileStore.LoadActiveProfile();
        if (profile is null)
        {
            Logs.Print("Secret unlock failed. Reason=NotConfigured.");
            return SecretSessionUnlockResult.NotConfigured();
        }

        var unlockResult = _cryptoService.Unlock(profile, masterPassword);
        if (!unlockResult.IsSuccess || unlockResult.DataKey is null)
        {
            Logs.Print($"Secret unlock failed. Reason={unlockResult.FailureReason}.");
            return SecretSessionUnlockResult.Failed(
                unlockResult.FailureReason ?? SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile);
        }

        var dataKey = unlockResult.DataKey;
        var dataKeyTransferredToSession = false;

        try
        {
            _secretSession.Unlock(dataKey, showSecrets);
            dataKeyTransferredToSession = true;
            Logs.Print($"Secret unlock succeeded. ShowSecrets={showSecrets}.");
            return SecretSessionUnlockResult.Unlocked();
        }
        finally
        {
            if (!dataKeyTransferredToSession)
                dataKey.Dispose();
        }
    }
}
