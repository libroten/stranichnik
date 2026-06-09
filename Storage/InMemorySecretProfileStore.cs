using System;
using Stranichnik.Security;

namespace Stranichnik.Storage;

public sealed class InMemorySecretProfileStore : ISecretProfileStore
{
    private CryptoProfileRecord? _profile;

    public CryptoProfileRecord? LoadActiveProfile()
    {
        return _profile;
    }

    public CryptoProfileRecord SaveNewProfile(CryptoProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_profile is not null)
            throw new InvalidOperationException("Secret crypto profile already exists.");

        _profile = profile;
        return profile;
    }

    public CryptoProfileRecord UpdateProfile(CryptoProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_profile is null)
            throw new InvalidOperationException("Secret crypto profile does not exist.");

        if (_profile.Id != profile.Id)
            throw new InvalidOperationException("Secret crypto profile ID cannot be changed.");

        _profile = profile;
        return profile;
    }

    internal bool DeleteActiveProfileForMasterPasswordReset(string secretGenerationId)
    {
        if (_profile is null ||
            !string.Equals(_profile.SecretGenerationId, secretGenerationId, StringComparison.Ordinal))
        {
            return false;
        }

        _profile = null;
        return true;
    }
}
