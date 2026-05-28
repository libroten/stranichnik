using System;
using System.IO;

namespace Stranichnik.Settings;

public static class AppDataPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(appData, "Stranichnik");
        }
    }

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static string LogPath => Path.Combine(AppDataDirectory, "stranichnik.log");
}
