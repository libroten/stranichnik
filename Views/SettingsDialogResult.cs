namespace Stranichnik.Views;

public sealed record SettingsDialogResult(
    bool Succeeded,
    bool WasCreated,
    string? ErrorMessage)
{
    public static SettingsDialogResult Created()
    {
        return new SettingsDialogResult(true, true, null);
    }

    public static SettingsDialogResult Changed()
    {
        return new SettingsDialogResult(true, false, null);
    }

    public static SettingsDialogResult Reset()
    {
        return new SettingsDialogResult(true, false, null);
    }

    public static SettingsDialogResult Failed(string errorMessage)
    {
        return new SettingsDialogResult(false, false, errorMessage);
    }
}
