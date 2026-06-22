using System;
using System.Diagnostics.CodeAnalysis;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretMasterPasswordChangeService
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the profile store abstraction so SQLite and tests can use different implementations.")]
    private readonly ISecretProfileStore _profileStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the crypto abstraction so password changes can be tested independently.")]
    private readonly ISecretCryptoService _cryptoService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the secret session abstraction so session state can be tested independently.")]
    private readonly ISecretSessionService _secretSession;
    private readonly Func<DateTimeOffset> _clock;

    public SecretMasterPasswordChangeService(
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

    public SecretMasterPasswordChangeResult ChangeMasterPassword(string newMasterPassword)
    {
        var profile = _profileStore.LoadActiveProfile();
        if (profile is null)
        {
            Logs.Print("Secret master password change failed. Reason=NotConfigured.");
            return SecretMasterPasswordChangeResult.Failed(
                SecretMasterPasswordChangeFailureReason.NotConfigured);
        }

        var dataKey = _secretSession.BorrowDataKey();
        if (dataKey is null)
        {
            Logs.Print("Secret master password change failed. Reason=Locked.");
            return SecretMasterPasswordChangeResult.Failed(
                SecretMasterPasswordChangeFailureReason.Locked);
        }

        CryptoProfileRecord updatedProfile;

        try
        {
            updatedProfile = _cryptoService.ChangeMasterPassword(
                profile,
                dataKey,
                newMasterPassword,
                _clock());
        }
        catch (SecretPayloadException exception)
            when (exception.FailureReason == SecretCryptoFailureReason.UnsupportedProfile)
        {
            Logs.Print("Secret master password change failed. Reason=UnsupportedProfile.");
            return SecretMasterPasswordChangeResult.Failed(
                SecretMasterPasswordChangeFailureReason.UnsupportedProfile);
        }

        try
        {
            _profileStore.UpdateProfile(updatedProfile);
            Logs.Print("Secret master password changed; crypto profile marked dirty for sync.");
            return SecretMasterPasswordChangeResult.Changed();
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Secret master password change failed. Reason=StoreUpdateFailed.");
            return SecretMasterPasswordChangeResult.Failed(
                SecretMasterPasswordChangeFailureReason.StoreUpdateFailed);
        }
    }
}
