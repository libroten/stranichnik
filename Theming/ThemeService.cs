using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Stranichnik.Theming;

public static class ThemeService
{
    public const string SystemTheme = "system";
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    private static bool _isTrackingSystemTheme;

    public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Light;

    public static ThemeMode CurrentEffectiveTheme { get; private set; } = ThemeMode.Light;

    public static ThemeMode NormalizeTheme(string? theme)
    {
        var normalizedTheme = theme?.Trim();

        if (string.Equals(normalizedTheme, SystemTheme, StringComparison.OrdinalIgnoreCase))
            return ThemeMode.System;

        if (string.Equals(normalizedTheme, DarkTheme, StringComparison.OrdinalIgnoreCase))
            return ThemeMode.Dark;

        return ThemeMode.Light;
    }

    public static string ToSettingsValue(ThemeMode theme)
    {
        return theme switch
        {
            ThemeMode.System => SystemTheme,
            ThemeMode.Dark => DarkTheme,
            _ => LightTheme
        };
    }

    public static void Apply(ThemeMode theme)
    {
        CurrentTheme = theme;
        ApplyPalette(ResolveEffectiveTheme(theme));
        EnsureSystemThemeTracking();
    }

    public static void Apply(string? theme)
    {
        Apply(NormalizeTheme(theme));
    }

    public static ThemeMode ResolveEffectiveTheme(ThemeMode theme)
    {
        if (theme != ThemeMode.System)
            return theme;

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeMode.Dark
            : ThemeMode.Light;
    }

    private static void EnsureSystemThemeTracking()
    {
        if (Application.Current is null)
            return;

        if (_isTrackingSystemTheme)
            return;

        Application.Current.ActualThemeVariantChanged += (_, _) =>
        {
            if (CurrentTheme == ThemeMode.System)
                ApplyPalette(ResolveEffectiveTheme(ThemeMode.System));
        };

        _isTrackingSystemTheme = true;
    }

    private static void ApplyPalette(ThemeMode effectiveTheme)
    {
        CurrentEffectiveTheme = effectiveTheme;

        if (Application.Current is null)
            return;

        var palette = effectiveTheme == ThemeMode.Dark
            ? ThemePalettes.Dark
            : ThemePalettes.Light;

        foreach (var color in palette.Colors)
            Application.Current.Resources[color.Key] = new SolidColorBrush(color.Value);
    }
}
