using System;
using System.Diagnostics.CodeAnalysis;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretProfileSetupService
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The setup service intentionally depends on the profile store abstraction so SQLite and tests can use different implementations.")]
    private readonly ISecretProfileStore _profileStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The setup service intentionally depends on the crypto abstraction so secret setup can be tested independently.")]
    private readonly ISecretCryptoService _cryptoService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The setup service intentionally depends on the secret session abstraction so session transitions can be tested independently.")]
    private readonly ISecretSessionService _secretSession;
    private readonly Func<DateTimeOffset> _clock;

    public SecretProfileSetupService(
        ISecretProfileStore profileStore,
        ISecretCryptoService cryptoService,
        ISecretSessionService secretSession,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(secretSession);

        _profileStore = profileStore;
        _cryptoService = cryptoService;
        _secretSession = secretSession;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public SecretProfileSetupResult CreateMasterPassword(
        string masterPassword,
        bool showSecrets)
    {
        if (_profileStore.LoadActiveProfile() is not null)
            return SecretProfileSetupResult.AlreadyConfigured();

        var creation = _cryptoService.CreateProfile(masterPassword, _clock());
        var dataKeyTransferredToSession = false;

        try
        {
            var profile = _profileStore.SaveNewProfile(creation.Profile);
            _secretSession.ConfigureAndUnlock(creation.DataKey, showSecrets);
            dataKeyTransferredToSession = true;
            Logs.Print($"Secret crypto profile created. ShowSecrets={showSecrets}.");
            return SecretProfileSetupResult.Created(profile);
        }
        finally
        {
            if (!dataKeyTransferredToSession)
                creation.DataKey.Dispose();
        }
    }
}
