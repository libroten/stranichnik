using System;

namespace Stranichnik.Sync.Credentials;

public sealed record SyncCredentials
{
    public SyncCredentials(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        Password = password;
    }

    public string Password { get; }
}
