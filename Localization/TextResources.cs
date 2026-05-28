using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Stranichnik.Localization;

public static class TextResources
{
    private static readonly ResourceManager ResourceManager = new(
        "Stranichnik.Resources.Strings",
        Assembly.GetExecutingAssembly());

    public static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }
}
