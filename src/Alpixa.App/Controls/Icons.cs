using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace Alpixa.App.Controls;

/// <summary>Stroke icons drawn with <see cref="Path"/> on a 24x24 grid, so they can be tinted per state.</summary>
public static class Icons
{
    public const string Dashboard = "M3 3h7v9H3z M14 3h7v5h-7z M14 12h7v9h-7z M3 16h7v5H3z";
    public const string Mail = "M3 5h18v14H3z M3 6l9 7 9-7";
    public const string Shield = "M12 2l8 3v6c0 5-3.4 9.3-8 11-4.6-1.7-8-6-8-11V5z M8.5 12l2.5 2.5 4.5-5";
    public const string Users = "M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1 M16 3.1a4 4 0 0 1 0 7.8 M22 21v-1a6 6 0 0 0-4-5.6";
    public const string File = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h5";
    public const string Send = "M22 2L11 13 M22 2l-7 20-4-9-9-4z";
    public const string Chart = "M3 3v18h18 M8 17v-5 M13 17V8 M18 17v-9";
    public const string Sliders = "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6";
    public const string Image = "M3 3h18v18H3z M8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z M21 15l-5-5L5 21";
    public const string Paperclip = "M21.4 11.1l-9.2 9.2a6 6 0 0 1-8.5-8.5l9.2-9.2a4 4 0 0 1 5.7 5.7l-9.2 9.2a2 2 0 0 1-2.8-2.8l8.5-8.5";
    public const string Upload = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12";
    public const string Sparkle = "M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z M19 17l.8 2.2L22 20l-2.2.8L19 23l-.8-2.2L16 20l2.2-.8z";

    public static Geometry Parse(string data) => (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!;

    public static string ForRoute(string? route) => route switch
    {
        "dashboard" => Dashboard,
        "profiles" => Mail,
        "domainhealth" => Shield,
        "lists" => Users,
        "templates" => File,
        "campaigns" => Send,
        "reports" => Chart,
        "settings" => Sliders,
        _ => Sparkle
    };
}

public sealed class RouteIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Icons.Parse(Icons.ForRoute(value as string));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Markup extension for XAML: <c>Data="{fx:Icon Image}"</c>.</summary>
[ContentProperty(nameof(Name))]
public sealed class IconExtension : IMarkupExtension<Geometry>
{
    public string Name { get; set; } = "";

    public Geometry ProvideValue(IServiceProvider serviceProvider)
        => Icons.Parse((string?)typeof(Icons).GetField(Name)?.GetValue(null) ?? Icons.Sparkle);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
