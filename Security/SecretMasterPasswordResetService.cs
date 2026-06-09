using System;
using System.Diagnostics.CodeAnalysis;
using Stranichnik.Diagnostics;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretMasterPasswordResetService
{
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the profile store abstraction so SQLite and tests can use different implementations.")]
    private readonly ISecretProfileStore _profileStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the reset store abstraction so reset storage can be tested independently.")]
    private readonly ISecretResetStore _resetStore;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The service intentionally depends on the secret session abstraction so session state can be tested independently.")]
    private readonly ISecretSessionService _secretSession;

    public SecretMasterPasswordResetService(
        ISecretProfileStore profileStore,
        ISecretResetStore resetStore,
        ISecretSessionService secretSession)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(resetStore);
        ArgumentNullException.ThrowIfNull(secretSession);

        _profileStore = profileStore;
        _resetStore = resetStore;
        _secretSession = secretSession;
    }

    public SecretMasterPasswordResetResult ResetMasterPasswordAndDeleteSecrets()
    {
        var profile = _profileStore.LoadActiveProfile();
        if (profile is null)
        {
            Logs.Print("Secret master password reset skipped. Reason=NotConfigured.");
            return SecretMasterPasswordResetResult.Failed(
                SecretMasterPasswordResetFailureReason.NotConfigured);
        }

        try
        {
            Logs.Print("Secret master password reset started.");
            var result = _resetStore.ResetMasterPasswordAndPurgeSecrets(profile.SecretGenerationId);
            _secretSession.MarkNotConfigured();
            Logs.Print("Secret master password reset completed.");
            return SecretMasterPasswordResetResult.Reset(result.PurgedSecretBookmarkCount);
        }
        catch (ArgumentException)
        {
            Logs.Print("Secret master password reset failed. Reason=StoreResetFailed.");
            return SecretMasterPasswordResetResult.Failed(
                SecretMasterPasswordResetFailureReason.StoreResetFailed);
        }
        catch (InvalidOperationException)
        {
            Logs.Print("Secret master password reset failed. Reason=StoreResetFailed.");
            return SecretMasterPasswordResetResult.Failed(
                SecretMasterPasswordResetFailureReason.StoreResetFailed);
        }
    }
}

