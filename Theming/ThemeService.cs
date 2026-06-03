using System;
using Avalonia;
using Avalonia.Media;

namespace Stranichnik.Theming;

public static class ThemeService
{
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Light;

    public static ThemeMode NormalizeTheme(string? theme)
    {
        return string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase)
            ? ThemeMode.Dark
            : ThemeMode.Light;
    }

    public static string ToSettingsValue(ThemeMode theme)
    {
        return theme == ThemeMode.Dark ? DarkTheme : LightTheme;
    }

    public static void Apply(ThemeMode theme)
    {
        CurrentTheme = theme;

        if (Application.Current is null)
            return;

        var palette = theme == ThemeMode.Dark
            ? ThemePalettes.Dark
            : ThemePalettes.Light;

        foreach (var color in palette.Colors)
            Application.Current.Resources[color.Key] = new SolidColorBrush(color.Value);
    }

    public static void Apply(string? theme)
    {
        Apply(NormalizeTheme(theme));
    }
}
