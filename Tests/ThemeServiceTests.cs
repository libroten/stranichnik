using Stranichnik.Theming;
using Xunit;

namespace Stranichnik.Tests;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("LIGHT", ThemeMode.Light)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("SYSTEM", ThemeMode.System)]
    [InlineData("", ThemeMode.Light)]
    [InlineData(null, ThemeMode.Light)]
    [InlineData("unknown", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("DARK", ThemeMode.Dark)]
    public void NormalizeTheme_maps_supported_and_unknown_themes(string? theme, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeService.NormalizeTheme(theme));
    }

    [Theory]
    [InlineData(ThemeMode.System, ThemeService.SystemTheme)]
    [InlineData(ThemeMode.Light, ThemeService.LightTheme)]
    [InlineData(ThemeMode.Dark, ThemeService.DarkTheme)]
    public void ToSettingsValue_returns_persisted_theme_value(ThemeMode theme, string expected)
    {
        Assert.Equal(expected, ThemeService.ToSettingsValue(theme));
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ResolveEffectiveTheme_keeps_explicit_theme(ThemeMode theme)
    {
        Assert.Equal(theme, ThemeService.ResolveEffectiveTheme(theme));
    }
}
