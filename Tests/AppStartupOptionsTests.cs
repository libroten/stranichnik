using Stranichnik;
using Xunit;

namespace Stranichnik.Tests;

public sealed class AppStartupOptionsTests
{
    [Fact]
    public void Configure_reads_simulated_credential_store_flag()
    {
        AppStartupOptions.Configure(["--simulate-unavailable-system-credential-store"]);

        Assert.True(AppStartupOptions.SimulateUnavailableSystemCredentialStore);

        AppStartupOptions.Configure([]);
    }

    [Fact]
    public void Configure_resets_simulated_credential_store_flag()
    {
        AppStartupOptions.Configure(["--simulate-unavailable-system-credential-store"]);
        AppStartupOptions.Configure([]);

        Assert.False(AppStartupOptions.SimulateUnavailableSystemCredentialStore);
    }
}
