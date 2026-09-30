using System.Globalization;
using Alpixa.App.Resources.Strings;

namespace Alpixa.App.Services;

public static class Loc
{
    public static string T(string key)
        => AppResources.ResourceManager.GetString(key, AppResources.Culture ?? CultureInfo.CurrentUICulture) ?? key;

    /// <summary>Returns null when the key does not exist (an empty value also counts as missing).</summary>
    public static string? TryGet(string key)
        => AppResources.ResourceManager.GetString(key, AppResources.Culture ?? CultureInfo.CurrentUICulture) is { Length: > 0 } s ? s : null;

    public static string T(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}

[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TrExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = "";

    public string ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
