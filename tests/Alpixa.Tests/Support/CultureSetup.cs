using System.Globalization;
using System.Runtime.CompilerServices;

namespace Alpixa.Tests.Support;

internal static class CultureSetup
{
    // Assertions check the Turkish (neutral) messages regardless of the machine's language.
    [ModuleInitializer]
    internal static void UseTurkish()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("tr-TR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
    }
}
