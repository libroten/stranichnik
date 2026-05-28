using System;
using System.Globalization;

namespace Stranichnik.Localization;

public static class LanguageService
{
    public const string EnglishLanguage = "en";
    public const string RussianLanguage = "ru";

    public static string CurrentLanguage
    {
        get
        {
            return NormalizeLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, RussianLanguage, StringComparison.OrdinalIgnoreCase) ||
            language?.StartsWith("ru-", StringComparison.OrdinalIgnoreCase) == true)
        {
            return RussianLanguage;
        }

        return EnglishLanguage;
    }

    public static void Apply(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language));

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
