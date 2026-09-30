using System.Globalization;
using Alpixa.App.Services;
using Alpixa.Core.Models;

namespace Alpixa.App.Converters;

public sealed class CheckStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CheckStatus.Pass => Color.FromArgb("#0E9F6E"),
        CheckStatus.Info => Color.FromArgb("#3F83F8"),
        CheckStatus.Warning => Color.FromArgb("#E3A008"),
        CheckStatus.Fail => Color.FromArgb("#E02424"),
        LintSeverity.Info => Color.FromArgb("#3F83F8"),
        LintSeverity.Warning => Color.FromArgb("#E3A008"),
        LintSeverity.Error => Color.FromArgb("#E02424"),
        _ => Colors.Gray
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Enum e ? Loc.T($"Enum_{e.GetType().Name}_{e}") : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;
}

public sealed class NotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? d.ToString("P2", culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("g", culture),
        _ => "-"
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HasItemsConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value is int i ? i > 0 : value is System.Collections.ICollection c && c.Count > 0;
        return Invert ? !has : has;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
