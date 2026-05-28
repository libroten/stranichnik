using System;
using System.Globalization;
using Stranichnik.Localization;
using Xunit;

namespace Stranichnik.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ru", LanguageService.RussianLanguage)]
    [InlineData("ru-RU", LanguageService.RussianLanguage)]
    [InlineData("RU-ru", LanguageService.RussianLanguage)]
    [InlineData("en", LanguageService.EnglishLanguage)]
    [InlineData("", LanguageService.EnglishLanguage)]
    [InlineData(null, LanguageService.EnglishLanguage)]
    [InlineData("de", LanguageService.EnglishLanguage)]
    public void NormalizeLanguage_maps_supported_and_unknown_languages(string? language, string expected)
    {
        Assert.Equal(expected, LanguageService.NormalizeLanguage(language));
    }

    [Fact]
    public void TextResourcesGet_returns_localized_value_for_existing_key()
    {
        using var cultureScope = new CultureScope("en");

        var value = TextResources.Get("Common_Save");

        Assert.Equal("Save", value);
    }

    [Fact]
    public void TextResourcesGet_returns_key_for_missing_key()
    {
        using var cultureScope = new CultureScope("en");

        var value = TextResources.Get("Missing_Key_For_Test");

        Assert.Equal("Missing_Key_For_Test", value);
    }

    [Fact]
    public void ConfirmDeleteBookmarkMessage_includes_bookmark_title()
    {
        using var cultureScope = new CultureScope("en");

        var message = UiStrings.ConfirmDeleteBookmarkMessage("Example");

        Assert.Contains("Example", message);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        public CultureScope(string cultureName)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;

            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
