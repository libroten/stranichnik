using System;

namespace Stranichnik;

public static class AppStartupOptions
{
    public static bool UseSampleData { get; private set; }

    public static bool SimulateUnavailableSystemCredentialStore { get; private set; }

    public static void Configure(string[] args)
    {
        UseSampleData = ContainsFlag(args, "--use-sample-data");
        SimulateUnavailableSystemCredentialStore = ContainsFlag(args, "--simulate-unavailable-system-credential-store");
    }

    private static bool ContainsFlag(string[] args, string flag)
    {
        return Array.Exists(
            args,
            arg => string.Equals(arg, flag, StringComparison.Ordinal));
    }
}
