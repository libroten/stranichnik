using System;
using System.Text.Json;
using Stranichnik.Settings;
using Stranichnik.Sync.Credentials;
using Xunit;

namespace Stranichnik.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void AppSettings_has_safe_sync_defaults()
    {
        var settings = new AppSettings();

        Assert.Equal(string.Empty, settings.Sync.WebDavUrl);
        Assert.Equal(string.Empty, settings.Sync.Username);
        Assert.Equal(string.Empty, settings.Sync.CredentialStorageKind);
        Assert.Null(settings.Sync.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public void AppSettings_sync_json_does_not_contain_raw_password_value()
    {
        var settings = new AppSettings
        {
            Sync = new SyncSettings
            {
                WebDavUrl = "https://example.invalid/webdav/",
                Username = "user",
                CredentialStorageKind = SyncCredentialStorageKindNames.SystemCredentialStore
            }
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }
}
