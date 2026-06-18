using System;

namespace Stranichnik.Settings;

public sealed class SyncSettings
{
    public bool IsEnabled { get; set; }

    public string WebDavUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }
}
