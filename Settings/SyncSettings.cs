using System;

namespace Stranichnik.Settings;

public sealed class SyncSettings
{
    public string WebDavUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string CredentialStorageKind { get; set; } = string.Empty;

    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }
}
