namespace Stranichnik.Settings;

public sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;

    public string Theme { get; set; } = string.Empty;

    public SyncSettings Sync { get; set; } = new();
}
