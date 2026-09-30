using System.Globalization;
using System.Resources;

namespace Alpixa.App.Resources.Strings;

/// <summary>
/// Access to AppResources*.resx. Written by hand instead of generated at build time, because the
/// generator is not run by the Windows (WinUI) build.
/// </summary>
public static class AppResources
{
    public static ResourceManager ResourceManager { get; } =
        new("Alpixa.App.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    public static CultureInfo? Culture { get; set; }
}
