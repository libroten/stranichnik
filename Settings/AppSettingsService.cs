using System;
using System.IO;
using System.Text.Json;

namespace Stranichnik.Settings;

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppDataPaths.SettingsPath))
                return new();

            var json = File.ReadAllText(AppDataPaths.SettingsPath);

            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new();
        }
        catch (IOException)
        {
            return new();
        }
        catch (UnauthorizedAccessException)
        {
            return new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataPaths.AppDataDirectory);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(AppDataPaths.SettingsPath, json);
    }
}
