using System;
using System.Text.Json;
using Stranichnik.Settings;
using Xunit;

namespace Stranichnik.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void AppSettings_has_safe_sync_defaults()
    {
        var settings = new AppSettings();

        Assert.False(settings.Sync.IsEnabled);
        Assert.Equal(string.Empty, settings.Sync.WebDavUrl);
        Assert.Equal(string.Empty, settings.Sync.Username);
        Assert.Null(settings.Sync.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public void AppSettings_sync_json_does_not_contain_password_field()
    {
        var settings = new AppSettings
        {
            Sync = new SyncSettings
            {
                IsEnabled = true,
                WebDavUrl = "https://example.invalid/webdav/",
                Username = "user"
            }
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
