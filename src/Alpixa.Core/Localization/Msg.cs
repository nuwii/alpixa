using System.Globalization;
using System.Resources;

namespace Alpixa.Core.Localization;

/// <summary>
/// Messages produced below the UI layer (domain checks, errors, content checks, reports). They follow
/// the UI language (CurrentUICulture); Turkish is the neutral language.
/// </summary>
public static class Msg
{
    private static readonly ResourceManager Resources = new("Alpixa.Core.Resources.Messages", typeof(Msg).Assembly);

    public static string T(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string T(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}
